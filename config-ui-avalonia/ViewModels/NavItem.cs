using CommunityToolkit.Mvvm.ComponentModel;

namespace MyKeymap.Settings.ViewModels;

/// <summary>
/// 导航条目 (复刻 NavigationDrawer.vue 的条目模型):
/// 标题 + 彩色徽标 (Vue 用 MDI 图标 + 哈希配色, 此处用首键字母徽标 + 同一套哈希配色,
/// 见 <see cref="NavBadge"/>) + 目标页。
/// </summary>
public sealed partial class NavItem : ObservableObject
{
    /// <summary>稳定标识 (重建导航时保持选中态): "home" / "action" / "keymap-{id}"。</summary>
    public required string Id { get; init; }

    /// <summary>显示标题 (keymap 条目为 config 里的 keymap.name, 数据驱动)。</summary>
    public required string Title { get; init; }

    /// <summary>徽标文字 (首键字母或语义符号)。</summary>
    public string Badge { get; init; } = "";

    /// <summary>徽标背景色 (十六进制, 按 Vue getColor 哈希算法计算)。</summary>
    public string BadgeColorHex { get; init; } = "#8E8E93";

    /// <summary>点击后展示的内容区页面对象 (各页面 ViewModel)。</summary>
    public required object Page { get; init; }

    /// <summary>是否当前选中 (由 MainViewModel 维护, 驱动高亮样式)。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// 徽标配色与文字逻辑, 复刻 NavigationDrawer.vue 的 getColor / getIcon 语义。
/// </summary>
public static class NavBadge
{
    // Vue colors = ["pink", "blue", "purple", "purple", "deep-orange", "purple", "blue"]
    // 对应 Material 色值
    private static readonly string[] Colors =
    [
        "#E91E63", // pink
        "#2196F3", // blue
        "#9C27B0", // purple
        "#9C27B0", // purple
        "#FF5722", // deep-orange
        "#9C27B0", // purple
        "#2196F3", // blue
    ];

    /// <summary>Vue getColor: 对首热键做 charCode*31 哈希, 取模选色。</summary>
    public static string ColorFor(string hotkey)
    {
        var hash = 0;
        foreach (var c in hotkey)
        {
            unchecked { hash = c + ((hash << 5) - hash); }
        }
        var index = (int)(Math.Abs((long)hash) % Colors.Length);
        return Colors[index];
    }

    /// <summary>父键优先: 有 parentID 时展示父 keymap 的热键 (复刻 getHotkey)。</summary>
    public static string EffectiveHotkey(Models.Keymap keymap, IReadOnlyList<Models.Keymap> allKeymaps)
    {
        if (keymap.ParentId != 0)
        {
            var parent = allKeymaps.FirstOrDefault(k => k.Id == keymap.ParentId);
            if (parent is not null) return parent.Hotkey;
        }
        return keymap.Hotkey;
    }

    /// <summary>
    /// 徽标文字, 复刻 getIcon 的语义分支 (无 MDI 图标, 用字符徽标表达同一含义):
    /// settings=⚙, customHotkeys=⌨, capslockAbbr=🚀, semicolonAbbr=✎, 鼠标=🖱,
    /// 数字/字母取首字符。
    /// </summary>
    public static string BadgeFor(string hotkey)
    {
        switch (hotkey)
        {
            case "settings": return "⚙";
            case "customHotkeys": return "⌨";
            case "capslockAbbr": return "🚀";
            case "semicolonAbbr": return "✎";
        }
        if (hotkey.Contains("button", StringComparison.OrdinalIgnoreCase)) return "🖱";

        // 去除开头的修饰符 (复刻 hotkey.replace(/^[^!#^+\w]/, ''))
        var h = hotkey.Length > 0 && "!#^+".IndexOf(hotkey[0]) < 0 && !char.IsLetterOrDigit(hotkey[0]) && hotkey[0] != '_'
            ? hotkey[1..]
            : hotkey;

        // 首字母 (L/R 前缀的按键取第二个字符, 复刻 getIcon)
        var key = h.Length > 0 ? h[..1] : "";
        if (key.Length > 0 && "LRlr".Contains(key) && h.Length > 1)
        {
            key = h[1..2];
        }
        key = key.ToLowerInvariant();
        return key.Length > 0 ? key.ToUpperInvariant() : "◆";
    }
}
