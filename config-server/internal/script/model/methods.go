package model

import (
	"fmt"
	"sort"
	"strings"
)

// ToAHKFuncArg 把配置值转成 AHK 函数参数字面量;
// "ahk-expression:" 前缀表示值本身是 AHK 表达式(遗留代码载荷), 原样输出
func ToAHKFuncArg(val string) string {
	prefix := "ahk-expression:"
	if strings.HasPrefix(val, prefix) {
		val = val[len(prefix):]
		return strings.TrimSpace(val)
	}
	return AhkString(val)
}

// AhkString 把字符串转成 AHK 字符串字面量 (转义反引号/双引号/空格后分号)
func AhkString(s string) string {
	s = strings.ReplaceAll(s, "`", "``")
	s = strings.ReplaceAll(s, "\"", "`\"")
	s = strings.ReplaceAll(s, " ;", " `;") // 空格后的分号会被 ahk 解释为注释
	return `"` + s + `"`
}

func NotBlankLines(str string) []string {
	var res []string
	for _, line := range strings.Split(str, "\n") {
		line = strings.TrimSpace(line)
		if line != "" {
			res = append(res, line)
		}
	}
	return res
}

func GroupName(id int, prefix ...string) string {
	p := "MY_WINDOW_GROUP_"
	if len(prefix) > 0 {
		p = prefix[0]
	}
	if id < 0 {
		return fmt.Sprintf(p+"_%d", -id)
	}
	return fmt.Sprintf(p+"%d", id)
}

func GroupToWinTile(g WindowGroup) string {
	if lines := NotBlankLines(g.Value); len(lines) > 1 {
		return fmt.Sprintf(`"ahk_group %s"`, GroupName(g.ID))
	} else {
		return fmt.Sprintf(`"%s"`, strings.TrimSpace(g.Value))
	}
}

func (c *Config) GetWinTitle(a Action) (winTitle string, conditionType int) {
	if a.WindowGroupID == 0 {
		return `""`, 0
	}

	for _, g := range c.Options.WindowGroups {
		if g.ID == a.WindowGroupID {
			// 5 表示自定义的 HotIf 表达式
			if g.ConditionType == 5 {
				return fmt.Sprintf(`'%s'`, g.Value), 5
			}

			return GroupToWinTile(g), g.ConditionType
		}
	}
	return `""`, 0
}

func (c *Config) GetHotkeyContext(a Action) string {
	winTitle, conditionType := c.GetWinTitle(a)
	if winTitle == `""` && conditionType == 0 {
		return ""
	}
	return fmt.Sprintf(", , %s, %d", winTitle, conditionType)
}

func (c *Config) EnabledKeymaps() []Keymap {
	var enabled []Keymap
	for _, km := range c.Keymaps {
		if km.ID == 1 && km.Enable {
			c.handleKeyRemapping(km)
			enabled = append(enabled, km)
		}
		if km.ID >= 5 && km.Enable {
			enabled = append(enabled, km)
		}
	}

	// 对模式进行分组和排序
	groups := make(map[int][]Keymap)
	for _, km := range enabled {
		if km.ParentID != 0 {
			groups[km.ParentID] = append(groups[km.ParentID], km)
		}
	}
	var res []Keymap
	for _, km := range enabled {
		if km.ParentID == 0 {
			res = append(res, km)
			if subKeymaps, ok := groups[km.ID]; ok {
				res = append(res, subKeymaps...)
			}
		}
	}
	return res
}

func (c *Config) handleKeyRemapping(custom Keymap) {
	// 把自定义热键中的按键重映射取出来, 这部分需要单独渲染
	var list []Action
	for hk, actions := range custom.Hotkeys {
		for i, a := range actions {
			if a.TypeID == RemapKey {
				a.Hotkey = hk
				list = append(list, a)
				a.RemapInHotIf = true
				actions[i] = a
			}
		}
	}

	// 按照 windowGroupID 进行排序
	sort.SliceStable(list, func(i, j int) bool {
		return list[i].WindowGroupID < list[j].WindowGroupID
	})

	var s strings.Builder
	lastGroup := -1 // 哨兵值, 保证第一组前写入 HotIf 头
	for _, a := range list {
		if lastGroup != a.WindowGroupID {
			s.WriteString("\n")
			s.WriteString(hotifHeader(c, a))
			s.WriteString("\n")
			lastGroup = a.WindowGroupID
		}
		s.WriteString(fmt.Sprintf("%s::%s\n", strings.TrimLeft(a.Hotkey, "*"), a.RemapToKey))
	}
	s.WriteString("\n#HotIf")
	c.KeyMapping = s.String()
}

func hotifHeader(c *Config, a Action) string {
	winTitle, conditionType := c.GetWinTitle(a)
	if winTitle == `""` && conditionType == 0 {
		return "#HotIf"
	}
	switch conditionType {
	case 1:
		return fmt.Sprintf("#HotIf WinActive(%s)", winTitle)
	case 2:
		return fmt.Sprintf("#HotIf WinExist(%s)", winTitle)
	case 3:
		return fmt.Sprintf("#HotIf !WinActive(%s)", winTitle)
	case 4:
		return fmt.Sprintf("#HotIf !WinExist(%s)", winTitle)
	case 5:
		return fmt.Sprintf("#HotIf %s ", winTitle)
	}
	return ""
}

func (c *Config) PathVariables() string {
	var s strings.Builder
	for _, v := range c.Options.PathVariables {
		if strings.TrimSpace(v.Name) == "" {
			continue
		}
		s.WriteString("  ")
		s.WriteString(v.Name)
		s.WriteString(" := ")
		s.WriteString(ToAHKFuncArg(v.Value))
		s.WriteString("\n")
	}
	return s.String()
}

func (c *Config) WindowGroups() string {
	var s strings.Builder
	for _, g := range c.Options.WindowGroups {
		addGroup(&s, g.Value, g.ID)
	}
	for _, km := range c.Keymaps {
		addGroup(&s, km.DisableAt, km.ID, "GROUP_DISABLE_KEYMAP_")
	}
	return s.String()
}

func (c *Config) GetKeymapDisableAt(kmID int) string {
	for _, km := range c.Keymaps {
		if km.ID == kmID {
			lines := NotBlankLines(km.DisableAt)
			switch {
			case len(lines) == 0:
				return ""
			case len(lines) == 1:
				return lines[0]
			case len(lines) > 1:
				return fmt.Sprintf("ahk_group GROUP_DISABLE_KEYMAP_%d", kmID)
			}
		}
	}
	return ""
}

func addGroup(s *strings.Builder, value string, id int, prefix ...string) {
	if lines := NotBlankLines(value); len(lines) > 1 {
		for _, v := range lines {
			arg := ToAHKFuncArg(v)
			if arg != `""` {
				s.WriteString(fmt.Sprintf("  GroupAdd(\"%s\", %s)\n", GroupName(id, prefix...), arg))
			}
		}

	}
}

func (c *Config) CapslockAbbr() map[string][]Action {
	for _, km := range c.Keymaps {
		if km.Hotkey == "capslockAbbr" {
			return km.Hotkeys
		}
	}
	return make(map[string][]Action)
}

func (c *Config) SemicolonAbbr() map[string][]Action {
	for _, km := range c.Keymaps {
		if km.Hotkey == "semicolonAbbr" {
			return km.Hotkeys
		}
	}
	return make(map[string][]Action)
}

func (c *Config) CapslockAbbrEnabled() bool {
	for _, km := range c.Keymaps {
		if !km.Enable {
			continue
		}
		for _, actions := range km.Hotkeys {
			for _, a := range actions {
				if a.TypeID == 9 && a.ValueID == 6 {
					return true
				}
			}
		}
	}
	return false
}

func (c *Config) CapslockAbbrKeys() string {
	var keys []string
	for key := range c.CapslockAbbr() {
		keys = append(keys, strings.ReplaceAll(key, ",", ",,"))
	}
	sort.Slice(keys, func(i, j int) bool {
		return keys[i] < keys[j]
	})
	return strings.Join(keys, ",")
}

func (c *Config) SemicolonAbbrEnabled() bool {
	for _, km := range c.Keymaps {
		if !km.Enable {
			continue
		}
		for _, actions := range km.Hotkeys {
			for _, a := range actions {
				if a.TypeID == 9 && a.ValueID == 5 {
					return true
				}
			}
		}
	}
	return false
}

func (c *Config) SemicolonAbbrKeys() string {
	var keys []string
	for key := range c.SemicolonAbbr() {
		keys = append(keys, strings.ReplaceAll(key, ",", ",,"))
	}
	sort.Slice(keys, func(i, j int) bool {
		return keys[i] < keys[j]
	})
	return strings.Join(keys, ",")
}
