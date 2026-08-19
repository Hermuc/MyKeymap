package script

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"sort"
	"strings"
)

// 刚刚发现 GoLand 可以直接把 JSON 字符串粘贴为「 结构体定义 」 一下省掉了好多工作

var MykeymapVersion string

type Config struct {
	Keymaps       []Keymap       `json:"keymaps,omitempty"`
	Options       Options        `json:"options,omitempty"`
	ActionSchemes []ActionScheme `json:"actionSchemes,omitempty"`
	HelpPageHtml  string         `json:"helpPageHtml,omitempty"` // 自定义帮助页 HTML, 保存时生成 bin/site/help.html
	KeyMapping    string         `json:"-"`
}

type Keymap struct {
	ID        int                 `json:"id"`
	Name      string              `json:"name"`
	Enable    bool                `json:"enable"`
	Hotkey    string              `json:"hotkey"`
	ParentID  int                 `json:"parentID"`
	Delay     int                 `json:"delay"`
	DisableAt string              `json:"disableAt"`
	Hotkeys   map[string][]Action `json:"hotkeys"`
}

// 选中动作方案: 选中文本/文件后按下快捷键执行预设行为
// 参考 RunAny 的「选中内容 + 快捷键触发预设行为」能力
// 变量约定: 在命令或脚本中使用 %selected% 表示当前选中的文本或文件路径(多文件用换行分隔)
type ActionScheme struct {
	ID     int          `json:"id"`
	Name   string       `json:"name"`
	Hotkey string       `json:"hotkey"`
	Enable bool         `json:"enable"`
	Rules  []ActionRule `json:"rules"`
}

// 规则按 Priority 升序匹配, 第一个匹配的规则生效
// MatchType: fileExt(文件后缀) / fileGroup(文件分组) / textRegex(文本正则) / textType(文本特征) / default(兜底)
// ActionType: open(程序打开) / search(搜索) / run(执行命令) / send_keys(发送按键) / script(AHK脚本) / copy(复制到剪贴板)
type ActionRule struct {
	Priority    int         `json:"priority"`
	MatchType   string      `json:"matchType"`
	MatchValue  string      `json:"matchValue"`
	ActionType  string      `json:"actionType"`
	ActionValue string      `json:"actionValue"`
	WorkingDir  string      `json:"workingDir,omitempty"`
	Options     RuleOptions `json:"options"`
}

type RuleOptions struct {
	CopyToClipboard bool `json:"copyToClipboard"` // 动作执行后把选中内容保留到剪贴板 (便于执行完成后直接粘贴)
	ClearSelection  bool `json:"clearSelection"`  // 执行后清空选中
	Confirm         bool `json:"confirm"`        // 执行前显示确认提示
}

type Action struct {
	WindowGroupID int    `json:"windowGroupID"`
	TypeID        int    `json:"actionTypeID"`
	Comment       string `json:"comment,omitempty"`
	Hotkey        string `json:"hotkey,omitempty"`
	// 下面的字段因动作类型而异
	KeysToSend         string `json:"keysToSend,omitempty"`
	RemapToKey         string `json:"remapToKey,omitempty"`
	ValueID            int    `json:"actionValueID,omitempty"`
	WinTitle           string `json:"winTitle,omitempty"`
	Target             string `json:"target,omitempty"`
	Args               string `json:"args,omitempty"`
	WorkingDir         string `json:"workingDir,omitempty"`
	RunAsAdmin         bool   `json:"runAsAdmin,omitempty"`
	RunInBackground    bool   `json:"runInBackground,omitempty"`
	DetectHiddenWindow bool   `json:"detectHiddenWindow,omitempty"`
	AHKCode            string `json:"ahkCode,omitempty"`

	RemapInHotIf bool `json:"-"`
}

func ParseConfig(file string) (*Config, error) {
	data, err := os.ReadFile(file)
	if err != nil {
		return nil, fmt.Errorf("cannot read file %s: %v", file, err)
	}

	var config Config
	err = json.Unmarshal(data, &config)
	if err != nil {
		return nil, fmt.Errorf("cannot parse config: %v", err)
	}

	config.Options.MykeymapVersion = MykeymapVersion
	if config.Options.Mouse.TipSymbol == "" {
		config.Options.Mouse.TipSymbol = "🐶"
	}
	if config.Options.CommandInputSkin == (CommandInputSkin{}) {
		config.Options.CommandInputSkin = CommandInputSkin{
			BackgroundColor:       "#FFFFFF",
			BackgroundOpacity:     "0.9",
			BorderWidth:           "3",
			BorderColor:           "#FFFFFF",
			BorderOpacity:         "1.0",
			BorderRadius:          "10",
			CornerColor:           "#000000",
			CornerOpacity:         "0.0",
			GridlineColor:         "#2843AD",
			GridlineOpacity:       "0.04",
			KeyColor:              "#000000",
			KeyOpacity:            "1.0",
			HideAnimationDuration: "0.34",
			WindowYPos:            "0.25",
			WindowWidth:           "700",
			WindowShadowColor:     "#000000",
			WindowShadowOpacity:   "0.5",
			WindowShadowSize:      "3.0",
		}
	}

	return &config, nil
}

func SaveConfigFile(config *Config) {
	// 先写到缓冲区,  如果直接写文件的话, 当编码过程遇到错误时, 会导致文件损坏
	buf := new(bytes.Buffer)
	encoder := json.NewEncoder(buf)
	encoder.SetIndent("", "  ")
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(config); err != nil {
		panic(err)
	}

	if err := os.WriteFile("../data/config.json", buf.Bytes(), 0644); err != nil {
		panic(err)
	}
}

func groupName(id int, prefix ...string) string {
	p := "MY_WINDOW_GROUP_"
	if len(prefix) > 0 {
		p = prefix[0]
	}
	if id < 0 {
		return fmt.Sprintf(p+"_%d", -id)
	}
	return fmt.Sprintf(p+"%d", id)
}

func groupToWinTile(g WindowGroup) string {
	if lines := notBlankLines(g.Value); len(lines) > 1 {
		return fmt.Sprintf(`"ahk_group %s"`, groupName(g.ID))
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

			return groupToWinTile(g), g.ConditionType
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
			if a.TypeID == remapKey {
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
