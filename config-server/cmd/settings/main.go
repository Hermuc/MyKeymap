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
	"syscall"

	"golang.org/x/sys/windows/registry"

	"settings/internal/command"
	"settings/internal/matrix"
	"settings/internal/script"
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
	// headless 模式: 供 Avalonia 壳以子进程方式拉起, 无代码雨/无浏览器/不开 CORS, 通过 stdout 端口通告行告知实际监听端口
	headless := len(os.Args) == 2 && os.Args[1] == "--headless"

	if !debug {
		if headless || hideMatrix() {
			close(rainDone)
			if !headless {
				fmt.Println("MyKeymap config server is running...")
			}
		} else {
			go matrix.DigitalRain(hasError, rainDone)
		}
	}
	if debug {
		hasError = nil
	}

	execCmd("./MyKeymap.exe", "/script", "./bin/MiscTools.ahk", "GenerateShortcuts")
	server(hasError, rainDone, debug, headless)
}

func server(hasError chan<- struct{}, rainDone <-chan struct{}, debug bool, headless bool) {
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

	if headless {
		// 端口通告行: 必须为 stdout 第一行输出, 供 Avalonia 壳逐行匹配 "MYKEYMAP_PORT=" 前缀 (不打印任何装饰文本)
		fmt.Printf("MYKEYMAP_PORT=%d\n", ln.Addr().(*net.TCPAddr).Port)
	}

	if !debug && !headless {
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
		// 旧 Vue web UI 已退役 (Avalonia 原生设置界面替代), site/ 不再包含 index.html:
		// 根路径直接返回 404, 避免 panic 拖垮后端服务
		c.Status(http.StatusNotFound)
		return
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
	// 以注册表真实生效态回填开机自启显示态 (详见 syncStartupFromRegistry)
	syncStartupFromRegistry(&config.Options.Startup)
	c.JSON(http.StatusOK, config)
}

// syncStartupFromRegistry 用注册表 Run 键的真实状态回填 options.startup。
// 注册表是开机自启的真实生效态 (bin/MiscTools.ahk RunAtStartup 写/删
// HKCU\...\CurrentVersion\Run 下的 MyKeymap 值), config.json 的 options.startup
// 仅是 UI 显示态且无同步机制, 外部删除注册表项后 UI 会显示失真, 故 GET /config
// 时以注册表为准回填。
// 特意不放进 ParseConfig: 它还服务于 GenerateAHK/DumpPlan 等验证路径, 需保持
// 确定性, 回填只应作用于对外 HTTP 响应。
// 值不存在 (Run 键或 MyKeymap 值缺失) → false; 其他读失败 (如权限) → 保持
// config 原值不动, 不报错。
func syncStartupFromRegistry(startup *bool) {
	key, err := registry.OpenKey(registry.CURRENT_USER, `Software\Microsoft\Windows\CurrentVersion\Run`, registry.QUERY_VALUE)
	if err != nil {
		if errors.Is(err, syscall.ERROR_FILE_NOT_FOUND) {
			*startup = false // Run 键不存在视为未自启
		}
		return
	}
	defer key.Close()
	if _, _, err := key.GetStringValue("MyKeymap"); err != nil {
		if errors.Is(err, syscall.ERROR_FILE_NOT_FOUND) {
			*startup = false // MyKeymap 值不存在视为未自启
		}
		return
	}
	*startup = true
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

// execCmd 启动子进程 (相对 ../ 工作目录); 返回是否成功启动。
// 返回值供需要感知结果的调用方使用 (如保存配置后重启 MyKeymap, 失败时经
// restartFailed 字段告知前端); 不关心结果的调用点可忽略返回值。
func execCmd(exe string, args ...string) bool {
	// 用 cmd.Dir 指定子进程工作目录, 避免修改全局 cwd 影响其他 goroutine (如 GetConfigHandler 读取相对路径)
	dir, err := filepath.Abs("../")
	if err != nil {
		log.Println("execCmd: 获取项目根目录失败:", err)
		return false
	}

	var c = exec.Command(exe, args...)
	c.Dir = dir
	// CREATE_BREAKAWAY_FROM_JOB: 设置界面 (Avalonia) 会将本进程置于 KILL_ON_JOB_CLOSE
	// 的 Job 中以防孤儿; 保存设置时重启的 MyKeymap 若留在 Job 内, 会在设置窗口关闭
	// (或界面进程异常退出) 时被连带终止, 表现为「保存设置后 MyKeymap 退出」。
	// 此处让拉起的进程脱离 Job; 失败时 (如外层 Job 未开放 BREAKAWAY_OK) 按场景回退。
	c.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x01000000} // CREATE_BREAKAWAY_FROM_JOB
	if err := c.Start(); err != nil {
		log.Println("execCmd: breakaway 启动", exe, "失败:", err)
		return fallbackExecCmd(dir, exe, args)
	}
	return true
}

// fallbackExecCmd: breakaway 失败后的降级启动。
// 无参数调用 (保存设置后的托盘重启) 改经 explorer.exe 中转: explorer 不在本进程的
// Job 层级内, 由它拉起的进程彻底脱离任何 Job, 保证托盘不被设置窗口关闭连带终止;
// 代价是目标不继承本进程的提权状态 (由 MyKeymap 启动器自行 RunAs 提权)。
// 带参数调用 (WindowSpy/GenerateShortcuts 等短暂工具进程) 保持普通启动。
func fallbackExecCmd(dir, exe string, args []string) bool {
	if len(args) == 0 {
		absExe, err := filepath.Abs(filepath.Join(dir, exe))
		if err == nil {
			c := exec.Command("explorer.exe", absExe)
			if err := c.Start(); err == nil {
				return true
			}
			log.Println("execCmd: explorer 中转启动", exe, "失败:", err)
		}
	}
	c := exec.Command(exe, args...)
	c.Dir = dir
	if err := c.Start(); err != nil {
		log.Println("execCmd: 启动", exe, "失败:", err)
		return false
	}
	return true
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

		// 校验文件分组表结构 (名称/显示名/后缀列表非空), 非法分组拒绝保存
		if err := script.ValidateFileGroups(config.FileGroups); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"message": "保存失败: " + err.Error()})
			return
		}

		script.SaveConfigFile(&config) // 保存配置文件

		if debug {
			script.GenerateScripts(&config) // 生成脚本文件
			// execCmd("./MyKeymap.exe", "./bin/MyKeymap.ahk") // 重启程序且跳过 ahk 脚本生成
		}
		// 重启程序, 此时 launcher 会重新生成脚本; 启动失败时经 restartFailed 告知前端
		// (旧前端不读该字段, 保持向后兼容)
		restartFailed := !execCmd("./MyKeymap.exe")

		c.JSON(http.StatusOK, gin.H{"message": "ok", "restartFailed": restartFailed})
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
