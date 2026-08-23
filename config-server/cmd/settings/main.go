package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"github.com/gin-contrib/cors"
	"github.com/gin-contrib/static"
	"github.com/gin-gonic/gin"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"settings/internal/command"
	"settings/internal/matrix"
	"settings/internal/script"
	"text/template"
)

func main() {
	if len(os.Args) >= 2 {
		if handler, ok := command.Map[os.Args[1]]; ok {
			handler(os.Args[2:]...)
			return
		}
	}

	hasError := make(chan struct{})
	rainDone := make(chan struct{})
	debug := len(os.Args) == 2 && os.Args[1] == "debug"

	if !debug {
		if hideMatrix() {
			close(rainDone)
			fmt.Println("MyKeymap config server is running...")
		} else {
			go matrix.DigitalRain(hasError, rainDone)
		}
	}
	if debug {
		hasError = nil
	}

	execCmd("./MyKeymap.exe", "/script", "./bin/MiscTools.ahk", "GenerateShortcuts")
	server(hasError, rainDone, debug)
}

func server(hasError chan<- struct{}, rainDone <-chan struct{}, debug bool) {
	if !debug {
		gin.SetMode(gin.ReleaseMode)
		gin.DefaultWriter = io.Discard
	}

	router := gin.Default()
	router.Use(PanicHandler(hasError, rainDone))
	if debug {
		router.Use(cors.Default())
	}
	router.NoRoute(static.Serve("/", static.LocalFile("./site", false)), indexHandler)

	router.GET("/", indexHandler)
	router.GET("/config", GetConfigHandler)
	router.PUT("/config", SaveConfigHandler(debug))
	router.POST("/server/command/:id", ServerCommandHandler)
	router.GET("/shortcuts", GetShortcutsHandler)

	// 选中动作方案 API
	router.GET("/api/action-schemes", GetActionSchemesHandler)
	router.GET("/api/action-schemes/:id", GetActionSchemeHandler)
	router.POST("/api/action-schemes", CreateActionSchemeHandler)
	router.PUT("/api/action-schemes/:id", UpdateActionSchemeHandler)
	router.DELETE("/api/action-schemes/:id", DeleteActionSchemeHandler)
	router.POST("/api/action-schemes/test", TestActionSchemeHandler)

	// 先尝试 12333 端口, 失败了则用随机端口. 因为 12333 端口可能已被占用, 或者被禁:
	// An attempt was made to access a socket in a way forbidden by its access permissions.
	ln, err := net.Listen("tcp", "localhost:12333")
	if err != nil {
		ln, err = net.Listen("tcp", "localhost:0")
		if err != nil {
			if hasError != nil { // debug 模式下 hasError 为 nil
				close(hasError)
			}
			<-rainDone
			fmt.Println("Error:", err.Error())
			_, _ = fmt.Scanln()
			os.Exit(1)
		}
	}

	if !debug {
		go func() {
			err := openBrowser(ln.Addr())
			if err != nil {
				hasError <- struct{}{}
				<-rainDone
				fmt.Println("Error:", err.Error())
			}
		}()
	}

	err = router.RunListener(ln)
	if err != nil {
		close(hasError)
		<-rainDone
		log.Fatal(err)
	}
}

func openBrowser(addr net.Addr) error {
	// 端口已就绪 (net.Listen 成功后调用), 无需固定延迟; 立即打开浏览器可显著缩短设置入口感知延迟
	if addr, ok := addr.(*net.TCPAddr); ok {
		// rundll32 位于系统 PATH 中, 无需硬编码绝对路径
		return exec.Command("rundll32.exe", "url.dll,FileProtocolHandler", fmt.Sprintf("http://localhost:%d", addr.Port)).Start()
	}
	return errors.New("addr is not tcp")
}

func indexHandler(c *gin.Context) {
	data, err := os.ReadFile("./site/index.html")
	if err != nil {
		panic(err)
	}
	// 设置 Cache-Control: no-store 禁用缓存
	c.Header("Cache-Control", "no-store")
	c.Data(http.StatusOK, "text/html; charset=utf-8", data)
}

func GetConfigHandler(c *gin.Context) {
	config, err := script.ParseConfig("../data/config.json")
	if err != nil {
		panic(err)
	}
	c.JSON(http.StatusOK, config)
}

func GetShortcutsHandler(c *gin.Context) {
	type shortcut struct {
		Path string `json:"path"`
	}
	exe, err := os.Executable()
	if err != nil {
		panic(err)
	}
	root := filepath.Dir(filepath.Dir(exe))
	pattern := filepath.Join(root, "shortcuts", "*.lnk")

	files, err := filepath.Glob(pattern)
	if err != nil {
		panic(err)
	}
	var data []shortcut
	for _, f := range files {
		data = append(data, shortcut{
			Path: f[len(root)+1:],
		})
	}
	c.JSON(http.StatusOK, data)
}

func PanicHandler(hasError chan<- struct{}, rainDone <-chan struct{}) gin.HandlerFunc {
	return func(c *gin.Context) {
		defer func() {
			if err := recover(); err != nil {
				// 不允许重复 close channel
				if hasError != nil {
					close(hasError)
					<-rainDone
					hasError = nil
				}
				panic(err)
			}
		}()

		c.Next()
	}
}

func ServerCommandHandler(c *gin.Context) {
	m := map[string]struct {
		exe  string
		args []string
	}{
		"2": {
			exe:  "./MyKeymap.exe",
			args: []string{"/script", "bin/WindowSpy.ahk"},
		},
		"3": {
			exe:  "./MyKeymap.exe",
			args: []string{"/script", "./bin/MiscTools.ahk", "RunAtStartup", "On"},
		},
		"4": {
			exe:  "./MyKeymap.exe",
			args: []string{"/script", "./bin/MiscTools.ahk", "RunAtStartup", "Off"},
		},
	}
	if c, ok := m[c.Param("id")]; ok {
		execCmd(c.exe, c.args...)
	}

	c.JSON(http.StatusOK, gin.H{})
}

func execCmd(exe string, args ...string) {
	// 用 cmd.Dir 指定子进程工作目录, 避免修改全局 cwd 影响其他 goroutine (如 GetConfigHandler 读取相对路径)
	dir, err := filepath.Abs("../")
	if err != nil {
		log.Println("execCmd: 获取项目根目录失败:", err)
		return
	}

	var c = exec.Command(exe, args...)
	c.Dir = dir
	// 不调用 Wait: 目标是常驻进程 (如 MyKeymap.exe), 等待会阻塞请求
	if err := c.Start(); err != nil {
		log.Println("execCmd: 启动", exe, "失败:", err)
	}
}

func SaveConfigHandler(debug bool) gin.HandlerFunc {
	return func(c *gin.Context) {
		var config script.Config
		if err := c.ShouldBindJSON(&config); err != nil {
			panic(err)
		}

		// 校验选中动作方案组合合法性 (textType 特征 -> 行为 必须语义匹配), 非法组合拒绝保存
		for i := range config.ActionSchemes {
			if err := script.ValidateActionSchemeRules(&config.ActionSchemes[i]); err != nil {
				c.JSON(http.StatusBadRequest, gin.H{"message": "保存失败: " + err.Error()})
				return
			}
		}

		// 生成帮助文件: 有自定义内容才生成, 内容被清空时删除旧文件避免残留
		if config.HelpPageHtml != "" {
			saveHelpPageHtml(config.HelpPageHtml)
		} else {
			_ = os.Remove("../bin/site/help.html")
		}

		script.SaveConfigFile(&config) // 保存配置文件

		if debug {
			script.GenerateScripts(&config) // 生成脚本文件
			execCmd("./MyKeymap.exe")       // 重启程序, 此时 launcher 会重新生成脚本
			// execCmd("./MyKeymap.exe", "./bin/MyKeymap.ahk") // 重启程序且跳过 ahk 脚本生成
		} else {
			execCmd("./MyKeymap.exe") // 重启程序, 此时 launcher 会重新生成脚本
		}

		c.JSON(http.StatusOK, gin.H{"message": "ok"})
	}
}

func saveHelpPageHtml(html string) {

	f, err := os.Create("../bin/site/help.html")
	if err != nil {
		panic(err)
	}
	defer func(f *os.File) {
		_ = f.Close()
	}(f)

	files := []string{
		"./templates/help.html",
	}

	ts, err := template.ParseFiles(files...)
	if err != nil {
		panic(err)
	}
	err = ts.Execute(f, map[string]string{"helpPageHtml": html})
	if err != nil {
		panic(err)
	}
}

func hideMatrix() bool {
	var config struct {
		Options struct {
			HideMatrix bool `json:"hideMatrix"`
		} `json:"options"`
	}

	data, err := os.ReadFile("../data/config.json")
	if err != nil {
		return false
	}

	err = json.Unmarshal(data, &config)
	if err != nil {
		return false
	}

	return config.Options.HideMatrix
}
