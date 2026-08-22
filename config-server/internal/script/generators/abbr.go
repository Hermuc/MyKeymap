package generators

import (
	"fmt"
	"settings/internal/script/model"
	"sort"
	"strings"
)

// TODO 缩写功能中不支持的操作:
// 重映射按键, 锁定当前模式, 暂停重启锁定, 鼠标操作, 窗口操作, ....
// 基本上只支持发送按键, 启动程序, 执行内置函数, 系统控制
//
// AbbrToCode 把缩写表渲染为 switch case 代码
func AbbrToCode(abbrMap map[string][]model.Action) string {
	// map 结构转成 list, 然后进行排序
	type Abbr struct {
		abbr    string
		actions []model.Action
	}
	var abbrList []Abbr
	for abbr, actions := range abbrMap {
		actions = sortActions(actions)
		abbrList = append(abbrList, Abbr{abbr, actions})
	}
	sort.Slice(abbrList, func(i, j int) bool {
		return abbrList[i].abbr < abbrList[j].abbr
	})

	var s strings.Builder
	for _, abbr := range abbrList {
		s.WriteString(fmt.Sprintf("    case \"%s\":\n", abbr.abbr))
		for _, a := range abbr.actions {
			if _, ok := ActionMap[a.TypeID]; !ok {
				continue
			}

			call := ActionMap[a.TypeID](a, true)
			winTitle, conditionType := Cfg.GetWinTitle(a)
			if conditionType == 5 {
				winTitle = strings.Trim(winTitle, "'")
			}
			if a.WindowGroupID == 0 {
				s.WriteString(fmt.Sprintf("      %s\n", call))
				continue
			}
			s.WriteString(fmt.Sprintf("      if matchWinTitleCondition(%s, %d) {\n", winTitle, conditionType))
			s.WriteString(fmt.Sprintf("        %s\n", call))
			s.WriteString(fmt.Sprintf("        return\n"))
			s.WriteString(fmt.Sprintf("      }\n"))
		}
	}
	return s.String()
}

func sortActions(actions []model.Action) []model.Action {
	// 把 a.WindowGroupID 为 0 的放在最后面, 因为这是默认生效的动作, 优先级最低
	res := make([]model.Action, 0, len(actions))
	var suffix []model.Action
	for _, a := range actions {
		if a.WindowGroupID == 0 {
			suffix = append(suffix, a)
		} else {
			res = append(res, a)
		}
	}
	res = append(res, suffix...)
	return res
}
