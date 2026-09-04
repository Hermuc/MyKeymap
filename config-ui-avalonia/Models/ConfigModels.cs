using System.Text.Json.Serialization;

namespace MyKeymap.Settings.Models;

// ============================================================================
// MyKeymap 配置数据模型 (C# 复刻)
//
// 权威蓝本: config-server/internal/script/model/types.go
// 约定:
//   1. 每个类的属性声明顺序与对应 Go 结构体的字段顺序严格一致;
//   2. [JsonPropertyName] 与 Go json tag 逐字对齐 (含大小写);
//   3. Go 侧 `json:"-"` 的字段 (Config.KeyMapping / Action.RemapInHotIf)
//      不参与 JSON 序列化, 故本模型不定义;
//   4. Go 侧带 omitempty 的字段 (如 actionSchemes / fileGroups
//      以及 Action 的大部分字段) 在 JSON 中可能缺失, 反序列化时落回
//      C# 属性默认值 (空集合 / 空串 / 0 / false), 模型一律以非空类型容忍缺失。
// ============================================================================

/// <summary>对应 Go struct Config。顶层配置, GET/PUT /config 的载荷。</summary>
public sealed class Config
{
    [JsonPropertyName("keymaps")]
    public List<Keymap> Keymaps { get; set; } = [];

    [JsonPropertyName("options")]
    public Options Options { get; set; } = new();

    // omitempty: 旧配置文件可能没有此字段, 缺失时为空列表
    [JsonPropertyName("actionSchemes")]
    public List<ActionScheme> ActionSchemes { get; set; } = [];

    // omitempty: 文件分组快捷填充数据, 缺失时为空列表
    [JsonPropertyName("fileGroups")]
    public List<FileGroup> FileGroups { get; set; } = [];

    // omitempty: 自定义总览页 Markdown, 缺失时为 ""; 非空时总览页优先展示自定义内容
    [JsonPropertyName("overviewDocMd")]
    public string OverviewDocMd { get; set; } = "";
}

/// <summary>对应 Go struct Keymap。单个键盘映射 (一页按键矩阵)。</summary>
public sealed class Keymap
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "";

    [JsonPropertyName("parentID")]
    public int ParentId { get; set; }

    [JsonPropertyName("delay")]
    public int Delay { get; set; }

    [JsonPropertyName("disableAt")]
    public string DisableAt { get; set; } = "";

    /// <summary>
    /// 前端专用标记 (对应 Vue Keymap.isNew, 不参与序列化):
    /// 新增的 keymap 首次选中 singlePress 时自动填入默认动作 (任务 #10 页面使用)。
    /// </summary>
    [JsonIgnore]
    public bool IsNew { get; set; }

    /// <summary>按键 -> 动作列表。键为按键名 (如 "a"、"*1"), 值为该按键绑定的动作序列。</summary>
    [JsonPropertyName("hotkeys")]
    public Dictionary<string, List<Action>> Hotkeys { get; set; } = new();
}

/// <summary>
/// 对应 Go struct ActionScheme。选中动作方案: 选中文本/文件后按下快捷键执行预设行为。
/// 变量约定: 命令或脚本中 %selected% 表示当前选中内容 (多文件用换行分隔)。
/// </summary>
public sealed class ActionScheme
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "";

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("rules")]
    public List<ActionRule> Rules { get; set; } = [];

    /// <summary>
    /// 传输层字段 (不落盘): 保存/新建方案后 Go 侧重启 MyKeymap 失败时随响应置 true。
    /// C# 侧可空: null 时序列化省略 (WhenWritingNull), 不会写入 PUT /config 载荷与导出 JSON。
    /// </summary>
    [JsonPropertyName("restartFailed")]
    public bool? RestartFailed { get; set; }
}

/// <summary>
/// 对应 Go struct FileGroup。文件分组: 前端「文件后缀」条件值的快捷填充数据
/// (选择分组 -> 展开为逗号分隔后缀列表), 引擎层不感知分组概念。
/// </summary>
public sealed class FileGroup
{
    /// <summary>分组标识 (英文, 如 image)。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>中文显示名 (如 图片)。</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>后缀列表 (不含点, 如 ["jpg","jpeg"])。</summary>
    [JsonPropertyName("exts")]
    public List<string> Exts { get; set; } = [];
}

/// <summary>
/// 对应 Go struct ActionRule。规则按 Priority 升序匹配, 第一个匹配的规则生效。
/// MatchType: fileExt / textType;
/// ActionType: open / search / run / send_keys / script / copy
///   + textType 专用: open_url / open_path / open_folder / magnet_download / open_registry
///   (textType 特征与行为的合法组合由 Go 侧 ValidateActionSchemeRules 约束)。
/// </summary>
public sealed class ActionRule
{
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("matchType")]
    public string MatchType { get; set; } = "";

    [JsonPropertyName("matchValue")]
    public string MatchValue { get; set; } = "";

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("actionValue")]
    public string ActionValue { get; set; } = "";

    // omitempty: 仅 run 等动作类型使用
    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = "";

    [JsonPropertyName("options")]
    public RuleOptions Options { get; set; } = new();
}

/// <summary>对应 Go struct RuleOptions。</summary>
public sealed class RuleOptions
{
    /// <summary>动作执行后把选中内容保留到剪贴板 (便于执行完成后直接粘贴)。</summary>
    [JsonPropertyName("copyToClipboard")]
    public bool CopyToClipboard { get; set; }

    /// <summary>执行后清空选中。</summary>
    [JsonPropertyName("clearSelection")]
    public bool ClearSelection { get; set; }

    /// <summary>执行前显示确认提示。</summary>
    [JsonPropertyName("confirm")]
    public bool Confirm { get; set; }
}

/// <summary>对应 Go struct Action。按键绑定的单个动作, 部分字段因动作类型而异。</summary>
public sealed class Action
{
    [JsonPropertyName("windowGroupID")]
    public int WindowGroupId { get; set; }

    // json tag 为 actionTypeID (非 typeID), 与 Go 侧一致
    [JsonPropertyName("actionTypeID")]
    public int TypeId { get; set; }

    // omitempty
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";

    // omitempty
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "";

    // ---- 下面的字段因动作类型而异 (均 omitempty) ----

    [JsonPropertyName("keysToSend")]
    public string KeysToSend { get; set; } = "";

    [JsonPropertyName("remapToKey")]
    public string RemapToKey { get; set; } = "";

    // json tag 为 actionValueID
    [JsonPropertyName("actionValueID")]
    public int ValueId { get; set; }

    [JsonPropertyName("winTitle")]
    public string WinTitle { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("args")]
    public string Args { get; set; } = "";

    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = "";

    [JsonPropertyName("runAsAdmin")]
    public bool RunAsAdmin { get; set; }

    [JsonPropertyName("runInBackground")]
    public bool RunInBackground { get; set; }

    [JsonPropertyName("detectHiddenWindow")]
    public bool DetectHiddenWindow { get; set; }

    [JsonPropertyName("ahkCode")]
    public string AhkCode { get; set; } = "";

    /// <summary>
    /// 前端专用的「未配置」哨兵标记 (对应 Vue Action.isEmpty, Go 无此字段, 不参与序列化):
    /// 动作编辑页新建插槽时置 true; 保存清洗 (ConfigSaver) 会剔除 isEmpty 的动作。
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty { get; set; }
}

/// <summary>对应 Go struct Options。全局选项。</summary>
public sealed class Options
{
    [JsonPropertyName("hideMatrix")]
    public bool HideMatrix { get; set; }

    [JsonPropertyName("mykeymapVersion")]
    public string MykeymapVersion { get; set; } = "";

    [JsonPropertyName("windowGroups")]
    public List<WindowGroup> WindowGroups { get; set; } = [];

    [JsonPropertyName("mouse")]
    public Mouse Mouse { get; set; } = new();

    [JsonPropertyName("scroll")]
    public Scroll Scroll { get; set; } = new();

    [JsonPropertyName("commandInputSkin")]
    public CommandInputSkin CommandInputSkin { get; set; } = new();

    [JsonPropertyName("pathVariables")]
    public List<PathVariable> PathVariables { get; set; } = [];

    [JsonPropertyName("startup")]
    public bool Startup { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("keyMapping")]
    public string KeyMapping { get; set; } = "";

    [JsonPropertyName("keyboardLayout")]
    public string KeyboardLayout { get; set; } = "";
}

/// <summary>对应 Go struct WindowGroup。窗口分组 (动作生效的窗口条件)。</summary>
public sealed class WindowGroup
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // omitempty
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    // omitempty
    [JsonPropertyName("conditionType")]
    public int ConditionType { get; set; }
}

/// <summary>对应 Go struct Mouse。鼠标模式参数。</summary>
public sealed class Mouse
{
    [JsonPropertyName("keepMouseMode")]
    public bool KeepMouseMode { get; set; }

    [JsonPropertyName("showTip")]
    public bool ShowTip { get; set; }

    [JsonPropertyName("tipSymbol")]
    public string TipSymbol { get; set; } = "";

    [JsonPropertyName("delay1")]
    public string Delay1 { get; set; } = "";

    [JsonPropertyName("delay2")]
    public string Delay2 { get; set; } = "";

    [JsonPropertyName("fastSingle")]
    public string FastSingle { get; set; } = "";

    [JsonPropertyName("fastRepeat")]
    public string FastRepeat { get; set; } = "";

    [JsonPropertyName("slowSingle")]
    public string SlowSingle { get; set; } = "";

    [JsonPropertyName("slowRepeat")]
    public string SlowRepeat { get; set; } = "";
}

/// <summary>对应 Go struct Scroll。滚动模式参数。</summary>
public sealed class Scroll
{
    [JsonPropertyName("delay1")]
    public string Delay1 { get; set; } = "";

    [JsonPropertyName("delay2")]
    public string Delay2 { get; set; } = "";

    [JsonPropertyName("onceLineCount")]
    public string OnceLineCount { get; set; } = "";
}

/// <summary>对应 Go struct PathVariable。路径变量 (命令中可引用)。</summary>
public sealed class PathVariable
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

/// <summary>对应 Go struct CommandInputSkin。命令输入窗口外观 (数值均为字符串)。</summary>
public sealed class CommandInputSkin
{
    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "";

    [JsonPropertyName("backgroundOpacity")]
    public string BackgroundOpacity { get; set; } = "";

    [JsonPropertyName("borderWidth")]
    public string BorderWidth { get; set; } = "";

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; set; } = "";

    [JsonPropertyName("borderOpacity")]
    public string BorderOpacity { get; set; } = "";

    [JsonPropertyName("borderRadius")]
    public string BorderRadius { get; set; } = "";

    [JsonPropertyName("cornerColor")]
    public string CornerColor { get; set; } = "";

    [JsonPropertyName("cornerOpacity")]
    public string CornerOpacity { get; set; } = "";

    [JsonPropertyName("gridlineColor")]
    public string GridlineColor { get; set; } = "";

    [JsonPropertyName("gridlineOpacity")]
    public string GridlineOpacity { get; set; } = "";

    [JsonPropertyName("keyColor")]
    public string KeyColor { get; set; } = "";

    [JsonPropertyName("keyOpacity")]
    public string KeyOpacity { get; set; } = "";

    [JsonPropertyName("hideAnimationDuration")]
    public string HideAnimationDuration { get; set; } = "";

    [JsonPropertyName("windowYPos")]
    public string WindowYPos { get; set; } = "";

    [JsonPropertyName("windowWidth")]
    public string WindowWidth { get; set; } = "";

    [JsonPropertyName("windowShadowColor")]
    public string WindowShadowColor { get; set; } = "";

    [JsonPropertyName("windowShadowOpacity")]
    public string WindowShadowOpacity { get; set; } = "";

    [JsonPropertyName("windowShadowSize")]
    public string WindowShadowSize { get; set; } = "";
}
