package generators

import (
	"fmt"
	"strings"

	"settings/internal/behaviors"
	"settings/internal/script/model"
)

// BehaviorCatalog 渲染期行为目录, 由调用方注入 (script.GenerateScripts / command.GenerateAHK /
// command.DumpPlan)。为 nil 或规则引用内置基础动作 ID 时直通 —— golden 快照与既有配置
// 的 DumpPlan 因此逐字节稳定。
var BehaviorCatalog *behaviors.Catalog

// actionSchemesCode 把 actionSchemes 配置渲染为 AHK 代码 (用于 mykeymap.tmpl 模板)
// 生成的数据结构由 bin/lib/rules/SelectedAction.ahk 中的 InitActionScheme 消费
func actionSchemesCode(schemes []model.ActionScheme) string {
	if len(schemes) == 0 {
		return ""
	}

	var buf strings.Builder
	buf.WriteString("  ; ===== 选中动作方案 =====\n")
	buf.WriteString("  ActionSchemeList := Array(\n")
	for _, s := range schemes {
		if !s.Enable || s.Hotkey == "" {
			continue
		}
		buf.WriteString(fmt.Sprintf("    {id: %d, name: %s, hotkey: %s, rules: Array(\n",
			s.ID, model.AhkString(s.Name), model.AhkString(s.Hotkey)))
		for _, r := range s.Rules {
			// 行为 ID 解析: 内置动作直通; 用户包 builtin entry 展开为基础动作+包默认模板
			// (规则非空值优先, 空值补包默认); 与 plan.go planActionSchemes 的镜像逻辑一致
			actionType, actionValue, workingDir := behaviors.ResolveRuleAction(BehaviorCatalog, r.ActionType, r.ActionValue, r.WorkingDir)
			buf.WriteString(fmt.Sprintf("      {priority: %d, matchType: %s, matchValue: %s, actionType: %s, actionValue: %s, workingDir: %s, options: {copyToClipboard: %t, clearSelection: %t, confirm: %t}},\n",
				r.Priority, model.AhkString(r.MatchType), model.AhkString(r.MatchValue), model.AhkString(actionType),
				model.AhkString(actionValue), model.AhkString(workingDir),
				r.Options.CopyToClipboard, r.Options.ClearSelection, r.Options.Confirm))
		}
		buf.WriteString("    )},\n")
	}
	buf.WriteString("  )\n")
	buf.WriteString("  InitActionScheme(ActionSchemeList)\n")
	return buf.String()
}
