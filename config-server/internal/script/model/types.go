// Package model 承载 MyKeymap 配置的数据模型与纯配置方法。
// 依赖方向: generators -> model <- script, 避免生成逻辑与数据模型循环依赖。
package model

// RemapKey 是"重映射按键"动作的 TypeID, 生成端与渲染端共用
const RemapKey = 5

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
	Confirm         bool `json:"confirm"`         // 执行前显示确认提示
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

type Options struct {
	HideMatrix       bool             `json:"hideMatrix"`
	MykeymapVersion  string           `json:"mykeymapVersion"`
	WindowGroups     []WindowGroup    `json:"windowGroups"`
	Mouse            Mouse            `json:"mouse"`
	Scroll           Scroll           `json:"scroll"`
	CommandInputSkin CommandInputSkin `json:"commandInputSkin"`
	PathVariables    []PathVariable   `json:"pathVariables"`
	Startup          bool             `json:"startup"`
	Language         string           `json:"language"`
	KeyMapping       string           `json:"keyMapping"`
	KeyboardLayout   string           `json:"keyboardLayout"`
}

type WindowGroup struct {
	ID            int    `json:"id"`
	Name          string `json:"name"`
	Value         string `json:"value,omitempty"`
	ConditionType int    `json:"conditionType,omitempty"`
}

type Mouse struct {
	KeepMouseMode bool   `json:"keepMouseMode"`
	ShowTip       bool   `json:"showTip"`
	TipSymbol     string `json:"tipSymbol"`
	Delay1        string `json:"delay1"`
	Delay2        string `json:"delay2"`
	FastSingle    string `json:"fastSingle"`
	FastRepeat    string `json:"fastRepeat"`
	SlowSingle    string `json:"slowSingle"`
	SlowRepeat    string `json:"slowRepeat"`
}

type Scroll struct {
	Delay1        string `json:"delay1"`
	Delay2        string `json:"delay2"`
	OnceLineCount string `json:"onceLineCount"`
}

type PathVariable struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type CommandInputSkin struct {
	BackgroundColor       string `json:"backgroundColor"`
	BackgroundOpacity     string `json:"backgroundOpacity"`
	BorderWidth           string `json:"borderWidth"`
	BorderColor           string `json:"borderColor"`
	BorderOpacity         string `json:"borderOpacity"`
	BorderRadius          string `json:"borderRadius"`
	CornerColor           string `json:"cornerColor"`
	CornerOpacity         string `json:"cornerOpacity"`
	GridlineColor         string `json:"gridlineColor"`
	GridlineOpacity       string `json:"gridlineOpacity"`
	KeyColor              string `json:"keyColor"`
	KeyOpacity            string `json:"keyOpacity"`
	HideAnimationDuration string `json:"hideAnimationDuration"`
	WindowYPos            string `json:"windowYPos"`
	WindowWidth           string `json:"windowWidth"`
	WindowShadowColor     string `json:"windowShadowColor"`
	WindowShadowOpacity   string `json:"windowShadowOpacity"`
	WindowShadowSize      string `json:"windowShadowSize"`
}
