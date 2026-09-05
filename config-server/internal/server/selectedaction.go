package server

import (
	"net/http"

	"github.com/gin-gonic/gin"
	"settings/internal/behaviors"
	"settings/internal/script"
	"settings/internal/script/model"
)

// 选中动作单键分发 API (方案 D)
// 数据持久化在 ../data/config.json 顶层 selectedAction 字段, 与 PUT /config 共用同一份配置。
// 旧的 action-schemes CRUD 六路由 (loadActionSchemes/saveActionSchemes 等) 已随「单键分发」
// 重构移除; 存量配置由 script.ParseConfig 读时一次性迁移 (硬切不回写)。

// loadSelectedAction 读取磁盘配置中的 selectedAction (ParseConfig 保证非 nil, 迁移后返回)。
func loadSelectedAction() *model.SelectedAction {
	config, err := script.ParseConfig("../data/config.json")
	if err != nil {
		panic(err)
	}
	return config.SelectedAction
}

type selectedActionTestRequest struct {
	Content string `json:"content"`
	IsFile  bool   `json:"isFile"`
	// 前端编辑中的 selectedAction 快照 (未保存的修改也能测试, 不落盘);
	// 为空时回退读取磁盘配置 (镜像旧 TestActionSchemeHandler 的快照优先模式)
	SelectedAction *model.SelectedAction `json:"selectedAction"`
}

// TestSelectedActionHandler 模拟测试: 输入选中内容, 返回第一个命中的 mapping、
// 其 entries 菜单 (key 从 1 起) 与默认动作预览。
// 复用 MatchActionRule → 覆盖校验 → ResolveRuleAction → PreviewAction 语义:
//   - 覆盖校验: entry 行为必须存在且覆盖匹配前提, 非法组合拒绝测试 (与保存行为一致);
//   - preview: 取菜单第一项经 ResolveRuleAction 展开 (用户行为包补默认模板) 后生成,
//     与渲染语义一致; menu 展示的仍是 behavior ID 与显示名。
func TestSelectedActionHandler(c *gin.Context) {
	var req selectedActionTestRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		panic(err)
	}
	sa := req.SelectedAction
	if sa == nil {
		sa = loadSelectedAction()
	}
	// 校验组合合法性 (cat 为 nil 时校验内部容忍, 见 script.ValidateSelectedAction)
	if err := script.ValidateSelectedAction(sa, loadBehaviorCatalog()); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"message": err.Error()})
		return
	}
	m := script.MatchSelectedAction(sa, req.IsFile, req.Content)
	if m == nil {
		c.JSON(http.StatusOK, gin.H{"matched": false})
		return
	}
	cat := loadBehaviorCatalog()
	entryName := func(id string) string {
		if p := cat.Get(id); p != nil {
			return p.Name
		}
		return id
	}
	menu := make([]gin.H, 0, len(m.Entries))
	for i := range m.Entries {
		e := &m.Entries[i]
		menu = append(menu, gin.H{"key": i + 1, "behavior": e.Behavior, "name": entryName(e.Behavior)})
	}
	// 预览按解析后的实际动作生成 (用户行为 builtin entry 展开为基础动作+包默认模板)
	var preview string
	if len(m.Entries) > 0 {
		first := m.Entries[0]
		actionType, actionValue, workingDir := behaviors.ResolveRuleAction(cat, first.Behavior, first.ActionValue, first.WorkingDir)
		preview = script.PreviewAction(&script.ActionRule{ActionType: actionType, ActionValue: actionValue, WorkingDir: workingDir}, req.Content)
	}
	c.JSON(http.StatusOK, gin.H{
		"matched":    true,
		"matchType":  m.MatchType,
		"matchValue": m.MatchValue,
		"menu":       menu,
		"preview":    preview,
	})
}
