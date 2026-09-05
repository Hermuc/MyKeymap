package server

import (
	"net/http"
	"strconv"

	"github.com/gin-gonic/gin"
	"settings/internal/behaviors"
	"settings/internal/proc"
	"settings/internal/script"
)

// 选中动作方案 REST API
// 数据持久化在 ../data/config.json 的 actionSchemes 字段, 与 PUT /config 共用同一份配置
// 保存后会重启 MyKeymap, 使 launcher 重新生成脚本 (与 SaveConfigHandler 行为一致)

func loadActionSchemes() []script.ActionScheme {
	config, err := script.ParseConfig("../data/config.json")
	if err != nil {
		panic(err)
	}
	if config.ActionSchemes == nil {
		return []script.ActionScheme{}
	}
	return config.ActionSchemes
}

// saveActionSchemes 持久化方案到 config.json 并重启 MyKeymap;
// 返回重启是否成功 (false 时由 handler 在响应中置 restartFailed 告知前端)。
func saveActionSchemes(schemes []script.ActionScheme) bool {
	config, err := script.ParseConfig("../data/config.json")
	if err != nil {
		panic(err)
	}
	config.ActionSchemes = schemes
	script.SaveConfigFile(config)
	return proc.ExecCmd("./MyKeymap.exe")
}

func parseSchemeID(c *gin.Context) int {
	id, err := strconv.Atoi(c.Param("id"))
	if err != nil {
		panic(err)
	}
	return id
}

func GetActionSchemesHandler(c *gin.Context) {
	c.JSON(http.StatusOK, loadActionSchemes())
}

func GetActionSchemeHandler(c *gin.Context) {
	id := parseSchemeID(c)
	for _, s := range loadActionSchemes() {
		if s.ID == id {
			c.JSON(http.StatusOK, s)
			return
		}
	}
	c.JSON(http.StatusNotFound, gin.H{"message": "scheme not found"})
}

func CreateActionSchemeHandler(c *gin.Context) {
	var scheme script.ActionScheme
	if err := c.ShouldBindJSON(&scheme); err != nil {
		panic(err)
	}
	if err := script.ValidateActionSchemeRules(&scheme, loadBehaviorCatalog()); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"message": err.Error()})
		return
	}
	schemes := loadActionSchemes()
	maxID := 0
	for _, s := range schemes {
		if s.ID > maxID {
			maxID = s.ID
		}
	}
	scheme.ID = maxID + 1
	schemes = append(schemes, scheme)
	restartOK := saveActionSchemes(schemes)
	if !restartOK {
		scheme.RestartFailed = true // 仅影响响应 JSON, 不回写已落盘数据
	}
	c.JSON(http.StatusOK, scheme)
}

func UpdateActionSchemeHandler(c *gin.Context) {
	id := parseSchemeID(c)
	var scheme script.ActionScheme
	if err := c.ShouldBindJSON(&scheme); err != nil {
		panic(err)
	}
	if err := script.ValidateActionSchemeRules(&scheme, loadBehaviorCatalog()); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"message": err.Error()})
		return
	}
	schemes := loadActionSchemes()
	for i, s := range schemes {
		if s.ID == id {
			scheme.ID = id
			schemes[i] = scheme
			restartOK := saveActionSchemes(schemes)
			if !restartOK {
				scheme.RestartFailed = true // 仅影响响应 JSON, 不回写已落盘数据
			}
			c.JSON(http.StatusOK, scheme)
			return
		}
	}
	c.JSON(http.StatusNotFound, gin.H{"message": "scheme not found"})
}

func DeleteActionSchemeHandler(c *gin.Context) {
	id := parseSchemeID(c)
	schemes := loadActionSchemes()
	for i, s := range schemes {
		if s.ID == id {
			schemes = append(schemes[:i], schemes[i+1:]...)
			restartOK := saveActionSchemes(schemes)
			c.JSON(http.StatusOK, gin.H{"message": "ok", "restartFailed": !restartOK})
			return
		}
	}
	c.JSON(http.StatusNotFound, gin.H{"message": "scheme not found"})
}

type actionSchemeTestRequest struct {
	SchemeID int                    `json:"schemeId"`
	Content  string                 `json:"content"`
	IsFile   bool                   `json:"isFile"`
	// 前端编辑中的方案快照 (未保存的修改也能测试); 为空时回退读取磁盘配置
	Scheme   *script.ActionScheme   `json:"scheme"`
}

// TestActionSchemeHandler 模拟测试: 输入选中内容, 返回第一个匹配的规则与执行预览
func TestActionSchemeHandler(c *gin.Context) {
	var req actionSchemeTestRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		panic(err)
	}
	// 优先使用请求中的方案快照, 否则从磁盘加载
	var scheme *script.ActionScheme
	if req.Scheme != nil {
		scheme = req.Scheme
	} else {
		for _, s := range loadActionSchemes() {
			if s.ID == req.SchemeID {
				scheme = &s
				break
			}
		}
	}
	if scheme == nil {
		c.JSON(http.StatusNotFound, gin.H{"message": "scheme not found"})
		return
	}
	// 校验组合合法性: textType 特征 -> 行为 必须语义匹配 (非法组合拒绝测试, 与保存行为一致)
	if err := script.ValidateActionSchemeRules(scheme, loadBehaviorCatalog()); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"message": err.Error()})
		return
	}
	rule := script.MatchActionScheme(scheme, req.IsFile, req.Content)
	if rule == nil {
		c.JSON(http.StatusOK, gin.H{"matched": false})
		return
	}
	// 预览按解析后的实际动作生成 (用户行为 builtin entry 展开为基础动作+包默认模板),
	// 与渲染语义一致; rule 本身原样返回 (UI 展示的仍是行为 ID)
	actionType, actionValue, workingDir := behaviors.ResolveRuleAction(loadBehaviorCatalog(), rule.ActionType, rule.ActionValue, rule.WorkingDir)
	resolved := *rule
	resolved.ActionType = actionType
	resolved.ActionValue = actionValue
	resolved.WorkingDir = workingDir
	c.JSON(http.StatusOK, gin.H{
		"matched": true,
		"rule":    rule,
		"preview": script.PreviewAction(&resolved, req.Content),
	})
}
