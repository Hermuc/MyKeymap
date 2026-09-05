package server

import "settings/internal/script/model"

// ============ DTO 类型定义 ============
// 与 model 包结构体逐字段对应, JSON tag 完全一致。
// 差异: 排除 json:"-" 的计算态字段 (Config.KeyMapping, Action.RemapInHotIf),
// 它们永不出现在 HTTP wire 上。
// RestartFailed 保留在 DTO (纯传输字段, 仅 HTTP 响应使用);
// 同时保守保留在 model 中 (action-scheme 端点仍直接序列化 model, 见报告说明)。

type ConfigDTO struct {
	Keymaps       []KeymapDTO       `json:"keymaps"`
	Options       OptionsDTO        `json:"options,omitempty"`
	ActionSchemes []ActionSchemeDTO `json:"actionSchemes"`
	FileGroups    []FileGroupDTO    `json:"fileGroups"`
	OverviewDocMd string            `json:"overviewDocMd,omitempty"`
}

type KeymapDTO struct {
	ID        int                   `json:"id"`
	Name      string                `json:"name"`
	Enable    bool                  `json:"enable"`
	Hotkey    string                `json:"hotkey"`
	ParentID  int                   `json:"parentID"`
	Delay     int                   `json:"delay"`
	DisableAt string                `json:"disableAt"`
	Hotkeys   map[string][]ActionDTO `json:"hotkeys"`
}

type ActionSchemeDTO struct {
	ID     int              `json:"id"`
	Name   string           `json:"name"`
	Hotkey string           `json:"hotkey"`
	Enable bool             `json:"enable"`
	Rules  []ActionRuleDTO  `json:"rules"`

	// 传输层字段 (不落盘): 保存/新建方案后重启 MyKeymap 失败时由 handler 置 true 随响应返回;
	// omitempty 保证 false 时不序列化, config.json 落盘与导入导出均不受影响
	RestartFailed bool `json:"restartFailed,omitempty"`
}

type FileGroupDTO struct {
	Name  string   `json:"name"`
	Label string   `json:"label"`
	Exts  []string `json:"exts"`
}

type ActionRuleDTO struct {
	Priority    int             `json:"priority"`
	MatchType   string          `json:"matchType"`
	MatchValue  string          `json:"matchValue"`
	ActionType  string          `json:"actionType"`
	ActionValue string          `json:"actionValue"`
	WorkingDir  string          `json:"workingDir,omitempty"`
	Options     RuleOptionsDTO  `json:"options"`
}

type RuleOptionsDTO struct {
	CopyToClipboard bool `json:"copyToClipboard"`
	ClearSelection  bool `json:"clearSelection"`
	Confirm         bool `json:"confirm"`
}

type ActionDTO struct {
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
}

type OptionsDTO struct {
	HideMatrix       bool                 `json:"hideMatrix"`
	MykeymapVersion  string               `json:"mykeymapVersion"`
	WindowGroups     []WindowGroupDTO     `json:"windowGroups"`
	Mouse            MouseDTO             `json:"mouse"`
	Scroll           ScrollDTO            `json:"scroll"`
	CommandInputSkin CommandInputSkinDTO  `json:"commandInputSkin"`
	PathVariables    []PathVariableDTO    `json:"pathVariables"`
	Startup          bool                 `json:"startup"`
	Language         string               `json:"language"`
	KeyMapping       string               `json:"keyMapping"`
	KeyboardLayout   string               `json:"keyboardLayout"`
}

type WindowGroupDTO struct {
	ID            int    `json:"id"`
	Name          string `json:"name"`
	Value         string `json:"value,omitempty"`
	ConditionType int    `json:"conditionType,omitempty"`
}

type MouseDTO struct {
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

type ScrollDTO struct {
	Delay1        string `json:"delay1"`
	Delay2        string `json:"delay2"`
	OnceLineCount string `json:"onceLineCount"`
}

type PathVariableDTO struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type CommandInputSkinDTO struct {
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

// ============ model → DTO (GET 响应) ============

func ConfigToDTO(cfg *model.Config) *ConfigDTO {
	dto := &ConfigDTO{
		OverviewDocMd: cfg.OverviewDocMd,
		Options:       optionsToDTO(cfg.Options),
	}
	if cfg.Keymaps != nil {
		dto.Keymaps = make([]KeymapDTO, len(cfg.Keymaps))
		for i, km := range cfg.Keymaps {
			dto.Keymaps[i] = keymapToDTO(km)
		}
	} else {
		dto.Keymaps = []KeymapDTO{}
	}
	// 空集合恒输出 [] 而非缺键/	null: GET /config 的结构契约保证前端与测试可无条件取数组
	// (工厂默认配置可以没有选中动作方案与文件分组, 但响应结构不随配置内容漂移)
	if cfg.ActionSchemes != nil {
		dto.ActionSchemes = make([]ActionSchemeDTO, len(cfg.ActionSchemes))
		for i, s := range cfg.ActionSchemes {
			dto.ActionSchemes[i] = actionSchemeToDTO(s)
		}
	} else {
		dto.ActionSchemes = []ActionSchemeDTO{}
	}
	if cfg.FileGroups != nil {
		dto.FileGroups = make([]FileGroupDTO, len(cfg.FileGroups))
		for i, fg := range cfg.FileGroups {
			dto.FileGroups[i] = FileGroupDTO{Name: fg.Name, Label: fg.Label, Exts: fg.Exts}
		}
	} else {
		dto.FileGroups = []FileGroupDTO{}
	}
	return dto
}

func keymapToDTO(km model.Keymap) KeymapDTO {
	dto := KeymapDTO{
		ID:        km.ID,
		Name:      km.Name,
		Enable:    km.Enable,
		Hotkey:    km.Hotkey,
		ParentID:  km.ParentID,
		Delay:     km.Delay,
		DisableAt: km.DisableAt,
	}
	if km.Hotkeys != nil {
		dto.Hotkeys = make(map[string][]ActionDTO, len(km.Hotkeys))
		for k, actions := range km.Hotkeys {
			var dtoActions []ActionDTO
			if actions != nil {
				dtoActions = make([]ActionDTO, len(actions))
				for i, a := range actions {
					dtoActions[i] = actionToDTO(a)
				}
			}
			dto.Hotkeys[k] = dtoActions
		}
	}
	return dto
}

func actionToDTO(a model.Action) ActionDTO {
	return ActionDTO{
		WindowGroupID:      a.WindowGroupID,
		TypeID:             a.TypeID,
		Comment:            a.Comment,
		Hotkey:             a.Hotkey,
		KeysToSend:         a.KeysToSend,
		RemapToKey:         a.RemapToKey,
		ValueID:            a.ValueID,
		WinTitle:           a.WinTitle,
		Target:             a.Target,
		Args:               a.Args,
		WorkingDir:         a.WorkingDir,
		RunAsAdmin:         a.RunAsAdmin,
		RunInBackground:    a.RunInBackground,
		DetectHiddenWindow: a.DetectHiddenWindow,
		AHKCode:            a.AHKCode,
	}
}

func actionSchemeToDTO(s model.ActionScheme) ActionSchemeDTO {
	dto := ActionSchemeDTO{
		ID:            s.ID,
		Name:          s.Name,
		Hotkey:        s.Hotkey,
		Enable:        s.Enable,
		RestartFailed: s.RestartFailed,
	}
	if s.Rules != nil {
		dto.Rules = make([]ActionRuleDTO, len(s.Rules))
		for i, r := range s.Rules {
			dto.Rules[i] = actionRuleToDTO(r)
		}
	}
	return dto
}

func actionRuleToDTO(r model.ActionRule) ActionRuleDTO {
	return ActionRuleDTO{
		Priority:    r.Priority,
		MatchType:   r.MatchType,
		MatchValue:  r.MatchValue,
		ActionType:  r.ActionType,
		ActionValue: r.ActionValue,
		WorkingDir:  r.WorkingDir,
		Options: RuleOptionsDTO{
			CopyToClipboard: r.Options.CopyToClipboard,
			ClearSelection:  r.Options.ClearSelection,
			Confirm:         r.Options.Confirm,
		},
	}
}

func optionsToDTO(o model.Options) OptionsDTO {
	dto := OptionsDTO{
		HideMatrix:      o.HideMatrix,
		MykeymapVersion: o.MykeymapVersion,
		Mouse: MouseDTO{
			KeepMouseMode: o.Mouse.KeepMouseMode,
			ShowTip:       o.Mouse.ShowTip,
			TipSymbol:     o.Mouse.TipSymbol,
			Delay1:        o.Mouse.Delay1,
			Delay2:        o.Mouse.Delay2,
			FastSingle:    o.Mouse.FastSingle,
			FastRepeat:    o.Mouse.FastRepeat,
			SlowSingle:    o.Mouse.SlowSingle,
			SlowRepeat:    o.Mouse.SlowRepeat,
		},
		Scroll: ScrollDTO{
			Delay1:        o.Scroll.Delay1,
			Delay2:        o.Scroll.Delay2,
			OnceLineCount: o.Scroll.OnceLineCount,
		},
		CommandInputSkin: CommandInputSkinDTO{
			BackgroundColor:       o.CommandInputSkin.BackgroundColor,
			BackgroundOpacity:     o.CommandInputSkin.BackgroundOpacity,
			BorderWidth:           o.CommandInputSkin.BorderWidth,
			BorderColor:           o.CommandInputSkin.BorderColor,
			BorderOpacity:         o.CommandInputSkin.BorderOpacity,
			BorderRadius:          o.CommandInputSkin.BorderRadius,
			CornerColor:           o.CommandInputSkin.CornerColor,
			CornerOpacity:         o.CommandInputSkin.CornerOpacity,
			GridlineColor:         o.CommandInputSkin.GridlineColor,
			GridlineOpacity:       o.CommandInputSkin.GridlineOpacity,
			KeyColor:              o.CommandInputSkin.KeyColor,
			KeyOpacity:            o.CommandInputSkin.KeyOpacity,
			HideAnimationDuration: o.CommandInputSkin.HideAnimationDuration,
			WindowYPos:            o.CommandInputSkin.WindowYPos,
			WindowWidth:           o.CommandInputSkin.WindowWidth,
			WindowShadowColor:     o.CommandInputSkin.WindowShadowColor,
			WindowShadowOpacity:   o.CommandInputSkin.WindowShadowOpacity,
			WindowShadowSize:      o.CommandInputSkin.WindowShadowSize,
		},
		Startup:        o.Startup,
		Language:       o.Language,
		KeyMapping:     o.KeyMapping,
		KeyboardLayout: o.KeyboardLayout,
	}
	if o.WindowGroups != nil {
		dto.WindowGroups = make([]WindowGroupDTO, len(o.WindowGroups))
		for i, wg := range o.WindowGroups {
			dto.WindowGroups[i] = WindowGroupDTO{
				ID:            wg.ID,
				Name:          wg.Name,
				Value:         wg.Value,
				ConditionType: wg.ConditionType,
			}
		}
	}
	if o.PathVariables != nil {
		dto.PathVariables = make([]PathVariableDTO, len(o.PathVariables))
		for i, pv := range o.PathVariables {
			dto.PathVariables[i] = PathVariableDTO{Name: pv.Name, Value: pv.Value}
		}
	}
	return dto
}

// ============ DTO → model (PUT 请求) ============

func DTOToConfig(dto *ConfigDTO) *model.Config {
	cfg := &model.Config{
		OverviewDocMd: dto.OverviewDocMd,
		Options:       dtoToOptions(dto.Options),
	}
	if dto.Keymaps != nil {
		cfg.Keymaps = make([]model.Keymap, len(dto.Keymaps))
		for i, km := range dto.Keymaps {
			cfg.Keymaps[i] = dtoToKeymap(km)
		}
	}
	if dto.ActionSchemes != nil {
		cfg.ActionSchemes = make([]model.ActionScheme, len(dto.ActionSchemes))
		for i, s := range dto.ActionSchemes {
			cfg.ActionSchemes[i] = dtoToActionScheme(s)
		}
	}
	if dto.FileGroups != nil {
		cfg.FileGroups = make([]model.FileGroup, len(dto.FileGroups))
		for i, fg := range dto.FileGroups {
			cfg.FileGroups[i] = model.FileGroup{Name: fg.Name, Label: fg.Label, Exts: fg.Exts}
		}
	}
	return cfg
}

func dtoToKeymap(km KeymapDTO) model.Keymap {
	m := model.Keymap{
		ID:        km.ID,
		Name:      km.Name,
		Enable:    km.Enable,
		Hotkey:    km.Hotkey,
		ParentID:  km.ParentID,
		Delay:     km.Delay,
		DisableAt: km.DisableAt,
	}
	if km.Hotkeys != nil {
		m.Hotkeys = make(map[string][]model.Action, len(km.Hotkeys))
		for k, actions := range km.Hotkeys {
			var mActions []model.Action
			if actions != nil {
				mActions = make([]model.Action, len(actions))
				for i, a := range actions {
					mActions[i] = dtoToAction(a)
				}
			}
			m.Hotkeys[k] = mActions
		}
	}
	return m
}

func dtoToAction(a ActionDTO) model.Action {
	return model.Action{
		WindowGroupID:      a.WindowGroupID,
		TypeID:             a.TypeID,
		Comment:            a.Comment,
		Hotkey:             a.Hotkey,
		KeysToSend:         a.KeysToSend,
		RemapToKey:         a.RemapToKey,
		ValueID:            a.ValueID,
		WinTitle:           a.WinTitle,
		Target:             a.Target,
		Args:               a.Args,
		WorkingDir:         a.WorkingDir,
		RunAsAdmin:         a.RunAsAdmin,
		RunInBackground:    a.RunInBackground,
		DetectHiddenWindow: a.DetectHiddenWindow,
		AHKCode:            a.AHKCode,
	}
}

func dtoToActionScheme(s ActionSchemeDTO) model.ActionScheme {
	m := model.ActionScheme{
		ID:            s.ID,
		Name:          s.Name,
		Hotkey:        s.Hotkey,
		Enable:        s.Enable,
		RestartFailed: s.RestartFailed,
	}
	if s.Rules != nil {
		m.Rules = make([]model.ActionRule, len(s.Rules))
		for i, r := range s.Rules {
			m.Rules[i] = dtoToActionRule(r)
		}
	}
	return m
}

func dtoToActionRule(r ActionRuleDTO) model.ActionRule {
	return model.ActionRule{
		Priority:    r.Priority,
		MatchType:   r.MatchType,
		MatchValue:  r.MatchValue,
		ActionType:  r.ActionType,
		ActionValue: r.ActionValue,
		WorkingDir:  r.WorkingDir,
		Options: model.RuleOptions{
			CopyToClipboard: r.Options.CopyToClipboard,
			ClearSelection:  r.Options.ClearSelection,
			Confirm:         r.Options.Confirm,
		},
	}
}

func dtoToOptions(o OptionsDTO) model.Options {
	m := model.Options{
		HideMatrix:      o.HideMatrix,
		MykeymapVersion: o.MykeymapVersion,
		Mouse: model.Mouse{
			KeepMouseMode: o.Mouse.KeepMouseMode,
			ShowTip:       o.Mouse.ShowTip,
			TipSymbol:     o.Mouse.TipSymbol,
			Delay1:        o.Mouse.Delay1,
			Delay2:        o.Mouse.Delay2,
			FastSingle:    o.Mouse.FastSingle,
			FastRepeat:    o.Mouse.FastRepeat,
			SlowSingle:    o.Mouse.SlowSingle,
			SlowRepeat:    o.Mouse.SlowRepeat,
		},
		Scroll: model.Scroll{
			Delay1:        o.Scroll.Delay1,
			Delay2:        o.Scroll.Delay2,
			OnceLineCount: o.Scroll.OnceLineCount,
		},
		CommandInputSkin: model.CommandInputSkin{
			BackgroundColor:       o.CommandInputSkin.BackgroundColor,
			BackgroundOpacity:     o.CommandInputSkin.BackgroundOpacity,
			BorderWidth:           o.CommandInputSkin.BorderWidth,
			BorderColor:           o.CommandInputSkin.BorderColor,
			BorderOpacity:         o.CommandInputSkin.BorderOpacity,
			BorderRadius:          o.CommandInputSkin.BorderRadius,
			CornerColor:           o.CommandInputSkin.CornerColor,
			CornerOpacity:         o.CommandInputSkin.CornerOpacity,
			GridlineColor:         o.CommandInputSkin.GridlineColor,
			GridlineOpacity:       o.CommandInputSkin.GridlineOpacity,
			KeyColor:              o.CommandInputSkin.KeyColor,
			KeyOpacity:            o.CommandInputSkin.KeyOpacity,
			HideAnimationDuration: o.CommandInputSkin.HideAnimationDuration,
			WindowYPos:            o.CommandInputSkin.WindowYPos,
			WindowWidth:           o.CommandInputSkin.WindowWidth,
			WindowShadowColor:     o.CommandInputSkin.WindowShadowColor,
			WindowShadowOpacity:   o.CommandInputSkin.WindowShadowOpacity,
			WindowShadowSize:      o.CommandInputSkin.WindowShadowSize,
		},
		Startup:        o.Startup,
		Language:       o.Language,
		KeyMapping:     o.KeyMapping,
		KeyboardLayout: o.KeyboardLayout,
	}
	if o.WindowGroups != nil {
		m.WindowGroups = make([]model.WindowGroup, len(o.WindowGroups))
		for i, wg := range o.WindowGroups {
			m.WindowGroups[i] = model.WindowGroup{
				ID:            wg.ID,
				Name:          wg.Name,
				Value:         wg.Value,
				ConditionType: wg.ConditionType,
			}
		}
	}
	if o.PathVariables != nil {
		m.PathVariables = make([]model.PathVariable, len(o.PathVariables))
		for i, pv := range o.PathVariables {
			m.PathVariables[i] = model.PathVariable{Name: pv.Name, Value: pv.Value}
		}
	}
	return m
}
