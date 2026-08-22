package generators

import (
	"fmt"
	"settings/internal/script/model"
	"strings"
)

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
			buf.WriteString(fmt.Sprintf("      {priority: %d, matchType: %s, matchValue: %s, actionType: %s, actionValue: %s, workingDir: %s, options: {copyToClipboard: %t, clearSelection: %t, confirm: %t}},\n",
				r.Priority, model.AhkString(r.MatchType), model.AhkString(r.MatchValue), model.AhkString(r.ActionType),
				model.AhkString(r.ActionValue), model.AhkString(r.WorkingDir),
				r.Options.CopyToClipboard, r.Options.ClearSelection, r.Options.Confirm))
		}
		buf.WriteString("    )},\n")
	}
	buf.WriteString("  )\n")
	buf.WriteString("  InitActionScheme(ActionSchemeList)\n")
	return buf.String()
}
