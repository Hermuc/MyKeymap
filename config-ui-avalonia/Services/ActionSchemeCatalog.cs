using MyKeymap.Settings.Models;

namespace MyKeymap.Settings.Services;

// ============================================================================
// 选中动作系统常量 (复刻 config-ui/src/components/action/constants.ts)。
//
// 词表纪律: 合法性裁决永远以后端 400 为准 ——
//   TEXT_TYPE_ACTIONS / TEXT_TYPE_DEFAULT_ACTION 均为副本,
//   真源在 Go: config-server/internal/script/actionscheme.go 的 textTypeActions。
//   C# 侧词表仅用于展示候选与前端联动, 非法组合由后端保存/测试校验拦截。
//
// 文案键走 I18n (959+ 为移植新增编号), 语言切换时由页面 ViewModel 重建。
// ============================================================================

/// <summary>下拉选项条目 (Value 为配置值, Label 为显示文案; record 值相等便于 ComboBox 选中匹配)。</summary>
public sealed record ComboOption(string Value, string Label);

/// <summary>选中动作常量与工厂方法。</summary>
public static class ActionSchemeCatalog
{
    // 匹配条件类型 (复刻 MATCH_TYPES; 「文本正则」与「文件分组」已按 2026-08 改造移除,
    // 「默认 (兜底)」已按 2026-09 移除, 现仅剩 fileExt / textType 两类)
    // (Value, LabelKey, HintKey)
    public static readonly (string Value, string LabelKey, string HintKey)[] MatchTypes =
    [
        ("fileExt", "1031", "1034"),
        ("textType", "1032", "1035"),
    ];

    // 行为类型 (复刻 ACTION_TYPES; textType 下可选行为受 TextTypeActions 联动约束)
    // (Value, LabelKey, HintKey)
    public static readonly (string Value, string LabelKey, string HintKey)[] ActionTypes =
    [
        ("open_url", "1037", "1048"),
        ("open_path", "1038", "1049"),
        ("open_folder", "1039", "1050"),
        ("magnet_download", "1040", "1051"),
        ("open_registry", "1041", "1052"),
        ("open", "1042", "1053"),
        ("search", "1043", "1054"),
        ("run", "1044", "1055"),
        ("send_keys", "1045", "1056"),
        ("script", "1046", "1057"),
        ("copy", "1047", "1058"),
    ];

    // 文本特征 (复刻 TEXT_TYPES)
    public static readonly (string Value, string LabelKey)[] TextTypes =
    [
        ("url", "1059"),
        ("path", "1060"),
        ("magnet", "1061"),
        ("plain", "1062"),
    ];

    /// <summary>
    /// 文本特征 -> 可选行为类型 映射。
    /// 副本, 真源在 Go actionscheme.go 的 textTypeActions; 联动规则: 特征与行为必须语义匹配。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> TextTypeActions =
        new Dictionary<string, string[]>
        {
            ["url"] = ["open_url", "search"],
            ["path"] = ["open_path", "open_folder"],
            ["magnet"] = ["magnet_download"],
            ["plain"] = ["open_registry", "search", "run", "send_keys", "script", "copy"],
        };

    /// <summary>切换文本特征时的默认行为。副本, 真源语义对齐 Go textTypeActions 键集。</summary>
    public static readonly IReadOnlyDictionary<string, string> TextTypeDefaultAction =
        new Dictionary<string, string>
        {
            ["url"] = "open_url",
            ["path"] = "open_path",
            ["magnet"] = "magnet_download",
            ["plain"] = "search",
        };

    /// <summary>文本特征专用行为集合 (无命令模板, actionValue 恒为空; 复刻 TEXT_ACTIONS)。</summary>
    public static readonly HashSet<string> TextActions =
        ["open_url", "open_path", "open_folder", "magnet_download", "open_registry"];

    /// <summary>默认搜索模板 (复刻 DEFAULT_SEARCH_URL)。</summary>
    public const string DefaultSearchUrl = "https://www.google.com/search?q=%selected%";

    // ------------------------------------------------------------- 标签查询

    public static string MatchTypeLabel(string value)
        => MatchTypes.FirstOrDefault(x => x.Value == value) is var t && t.Value is not null
            ? I18n.T(t.LabelKey) : value;

    public static string ActionTypeLabel(string value)
        => ActionTypes.FirstOrDefault(x => x.Value == value) is var t && t.Value is not null
            ? I18n.T(t.LabelKey) : value;

    public static string TextTypeLabel(string value)
        => TextTypes.FirstOrDefault(x => x.Value == value) is var t && t.Value is not null
            ? I18n.T(t.LabelKey) : value;

    public static string MatchTypeHint(string value)
        => MatchTypes.FirstOrDefault(x => x.Value == value) is var t && t.Value is not null
            ? I18n.T(t.HintKey) : "";

    public static string ActionTypeHint(string value)
        => ActionTypes.FirstOrDefault(x => x.Value == value) is var t && t.Value is not null
            ? I18n.T(t.HintKey) : "";

    // ------------------------------------------------------------- 工厂方法

    /// <summary>新建一个空规则 (复刻 createRule, priority 由外部指定)。</summary>
    public static ActionRule CreateRule(int priority) => new()
    {
        Priority = priority,
        MatchType = "fileExt",
        MatchValue = "",
        ActionType = "open",
        ActionValue = "",
        WorkingDir = "",
        Options = new RuleOptions(),
    };

    /// <summary>新建一个默认方案 (复刻 createScheme, id 由后端分配)。</summary>
    public static ActionScheme CreateScheme() => new()
    {
        Id = 0,
        Name = I18n.T("1028"),
        Hotkey = "",
        Enable = true,
        Rules = [CreateRule(1)],
    };
}
