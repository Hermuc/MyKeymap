using Avalonia.Input;
using MyKeymap.Settings.Models;
// 避免与 Models.Action 歧义 (Keymap 模型引用需要 Models 命名空间)
using Action = System.Action;

namespace MyKeymap.Settings.Services;

// ============================================================================
// 热键捕获与格式化逻辑 (复刻 config-ui/src/components/action/constants.ts
// + HotkeyCapture.vue 的纯逻辑部分)。
//
// Vue 侧用浏览器 KeyboardEvent.code (ControlLeft/ControlRight 等) 区分左右
// 修饰键; Avalonia 侧用物理键枚举 (Key.LeftCtrl / Key.RightCtrl ...) 实现
// 等价逻辑 —— 比浏览器更容易区分左右。
//
// 设计: 全部纯函数 + 无 UI 依赖的状态机 (HotkeyCaptureCore), 便于单元测试。
// ============================================================================

/// <summary>静态热键工具: 物理键 -> AHK 键名、修饰前缀排序、显示格式化、冲突归一化。</summary>
public static class HotkeyLogic
{
    /// <summary>生成 AHK 热键时的修饰键固定顺序 (AHK 中修饰符顺序不影响功能, 固定顺序保证可读性; 复刻 MOD_ORDER)。</summary>
    public static readonly string[] ModOrder = ["<^", ">^", "<!", ">!", "<+", ">+", "<#", ">#"];

    /// <summary>修饰前缀 -> 捕获态显示名 (复刻 MODIFIER_CODE_MAP 的 label 列)。</summary>
    public static readonly IReadOnlyDictionary<string, string> ModPrefixLabels =
        new Dictionary<string, string>
        {
            ["<^"] = "LCtrl", [">^"] = "RCtrl",
            ["<!"] = "LAlt", [">!"] = "RAlt",
            ["<+"] = "LShift", [">+"] = "RShift",
            ["<#"] = "LWin", [">#"] = "RWin",
        };

    // AHK 修饰键前缀 -> 显示名 (复刻 constants.ts MODIFIER_MAP)
    private static readonly Dictionary<string, string> ModifierDisplayMap = new()
    {
        ["^"] = "Ctrl", ["!"] = "Alt", ["+"] = "Shift", ["#"] = "Win",
        ["<^"] = "LCtrl", ["<!"] = "LAlt", ["<+"] = "LShift", ["<#"] = "LWin",
        [">^"] = "RCtrl", [">!"] = "RAlt", [">+"] = "RShift", [">#"] = "RWin",
    };

    // AHK 键名 -> 显示名 (复刻 keyToDisplay)
    private static readonly Dictionary<string, string> KeyDisplayMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = "Space", ["esc"] = "Esc", ["enter"] = "Enter", ["tab"] = "Tab",
            ["up"] = "↑", ["down"] = "↓", ["left"] = "←", ["right"] = "→",
            ["pgup"] = "PageUp", ["pgdn"] = "PageDown",
            ["home"] = "Home", ["end"] = "End", ["ins"] = "Insert", ["del"] = "Delete",
            ["backspace"] = "Backspace", ["apps"] = "Menu",
        };

    /// <summary>
    /// 物理修饰键 -> (AHK 前缀, 显示名); 非修饰键返回 null。
    /// 复刻 MODIFIER_CODE_MAP: 左右侧各自独立前缀 (&lt;^/&gt;^ ...)。
    /// </summary>
    public static (string Prefix, string Label)? ModifierFor(Key key) => key switch
    {
        Key.LeftCtrl => ("<^", "LCtrl"),
        Key.RightCtrl => (">^", "RCtrl"),
        Key.LeftAlt => ("<!", "LAlt"),
        Key.RightAlt => (">!", "RAlt"),
        Key.LeftShift => ("<+", "LShift"),
        Key.RightShift => (">+", "RShift"),
        Key.LWin => ("<#", "LWin"),
        Key.RWin => (">#", "RWin"),
        _ => null,
    };

    /// <summary>是否修饰键 (捕获时修饰键暂存等待主键, 不单独成热键)。</summary>
    public static bool IsModifier(Key key) => ModifierFor(key) is not null;

    /// <summary>
    /// 物理键 -> AHK 键名 (复刻 keyToAhkName): 字母数字/功能键/常用符号;
    /// 不支持的键 (如反引号 OemTilde —— AHK Hotkey("``") 报 Invalid hotkey) 返回 null。
    /// </summary>
    public static string? KeyToAhkName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString().ToLowerInvariant();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key >= Key.F1 && key <= Key.F24) return $"F{key - Key.F1 + 1}";

        return key switch
        {
            Key.Space => "space",
            Key.Escape => "esc",
            Key.Enter => "enter",
            Key.Tab => "tab",
            Key.Up => "up",
            Key.Down => "down",
            Key.Left => "left",
            Key.Right => "right",
            Key.Home => "home",
            Key.End => "end",
            Key.PageUp => "pgup",
            Key.PageDown => "pgdn",
            Key.Insert => "ins",
            Key.Delete => "del",
            Key.Back => "backspace",
            // 主键盘符号键 (与 Vue keyToAhkName 的符号映射一致)
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemBackslash => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => null,
        };
    }

    /// <summary>
    /// 修饰键前缀列表 + 主键 -> AHK 格式热键 (复刻 buildAhkFromCodes):
    /// 前缀按 <see cref="ModOrder"/> 固定排序后拼接, 如 ["&gt;^","&lt;+"] + "q" -> "&lt;+&gt;^q"。
    /// </summary>
    public static string BuildAhk(IEnumerable<string> prefixes, string mainKey)
    {
        var ordered = prefixes
            .Where(p => Array.IndexOf(ModOrder, p) >= 0)
            .Distinct()
            .OrderBy(p => Array.IndexOf(ModOrder, p));
        return string.Concat(ordered) + mainKey;
    }

    /// <summary>
    /// 侧别归一化: 把左右侧别前缀 (&lt;^/&gt;^/&lt;!/&gt;!/&lt;+/&gt;+/&lt;#/&gt;#)
    /// 映射为通配两侧的单字符前缀 (^/!/+/#), 如 "&gt;^p" -&gt; "^p"、"&lt;^&gt;+k" -&gt; "^+k"。
    /// 背景: AHK 中带侧别前缀的热键只响应对应物理侧 —— 设置界面录制 "RCtrl+P" 后,
    /// 用户合理期望左 Ctrl+P 也能触发; 捕获提交时统一通配两侧, 与侧别无关的冲突
    /// 检测 (<see cref="NormalizeHotkey"/>) 语义保持一致。确需区分左右侧的进阶场景
    /// 可后续手改 config.json (AHK 语法 &lt;^/&gt;^ 依然合法)。
    /// </summary>
    public static string NormalizeSidePrefixes(string ahk)
    {
        if (string.IsNullOrEmpty(ahk) || ahk.Length < 2) return ahk ?? "";
        var sb = new System.Text.StringBuilder(ahk.Length);
        for (var i = 0; i < ahk.Length;)
        {
            // 两字符侧别前缀 (按 ModOrder 识别): 丢弃侧别标记, 保留修饰符本体
            if (i + 1 < ahk.Length && Array.IndexOf(ModOrder, ahk.Substring(i, 2)) >= 0)
            {
                sb.Append(ahk[i + 1]);
                i += 2;
            }
            else
            {
                sb.Append(ahk[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>AHK 格式热键 -> 可读格式 (复刻 ahkToDisplay): ^+q -> Ctrl+Shift+Q, &lt;^q -> LCtrl+Q。</summary>
    public static string AhkToDisplay(string? ahk)
    {
        if (string.IsNullOrEmpty(ahk)) return "";
        var parts = new List<string>();
        var rest = ahk;
        // 依次提取修饰键前缀 (先两字符左右前缀, 再单字符前缀)
        while (rest.Length >= 2 && ModifierDisplayMap.TryGetValue(rest[..2], out var two))
        {
            parts.Add(two);
            rest = rest[2..];
        }
        while (rest.Length >= 1 && ModifierDisplayMap.TryGetValue(rest[..1], out var one))
        {
            parts.Add(one);
            rest = rest[1..];
        }
        // 剩余部分: 去掉 * ~ $ 等前缀
        rest = rest.TrimStart('*', '~', '$');
        if (rest.Length > 0) parts.Add(KeyToDisplay(rest));
        return string.Join("+", parts);
    }

    private static string KeyToDisplay(string key)
        => KeyDisplayMap.TryGetValue(key, out var mapped)
            ? mapped
            : string.Concat(char.ToUpperInvariant(key[0]).ToString(), key.AsSpan(1));

    /// <summary>
    /// 归一化热键用于冲突比较 (复刻 normalizeHotkey): 去掉开头的 * ~ $ 以及左右修饰前缀 &lt; &gt;, 忽略大小写。
    /// 说明: &lt;^q / &gt;^q / ^q 归一化后均为 ^q, 冲突检测采用保守策略 (宁多报不放过)。
    /// </summary>
    public static string NormalizeHotkey(string hk)
        => hk.TrimStart('*', '~', '$', '<', '>').ToLowerInvariant();

    /// <summary>
    /// 仅去除通配前缀的弱归一化 (复刻 SelectedActionEdit.vue 里对「其他方案热键」的
    /// <c>hotkey.replace(/^[*~$]+/, "").toLowerCase()</c>): 保留左右修饰前缀。
    /// </summary>
    public static string StripWildcardPrefix(string hk)
        => hk.TrimStart('*', '~', '$').ToLowerInvariant();

    /// <summary>
    /// 收集已占用的热键 (复刻 collectUsedHotkeys): 启用的 keymap 的所有绑定键与触发键,
    /// 全部经 <see cref="NormalizeHotkey"/> 归一化 (排除 "settings"/"customHotkeys" 哨兵)。
    /// </summary>
    public static HashSet<string> CollectUsedHotkeys(IEnumerable<Keymap> keymaps)
    {
        var used = new HashSet<string>();
        foreach (var km in keymaps)
        {
            if (!km.Enable) continue;
            foreach (var hk in km.Hotkeys.Keys) used.Add(NormalizeHotkey(hk));
            if (!string.IsNullOrEmpty(km.Hotkey) && km.Hotkey != "settings" && km.Hotkey != "customHotkeys")
            {
                used.Add(NormalizeHotkey(km.Hotkey));
            }
        }
        return used;
    }
}

/// <summary>
/// 热键捕获状态机 (复刻 HotkeyCapture.vue 的捕获交互, 与 UI 解耦便于单测):
///   - StartCapture 进入捕获态;
///   - 修饰键按下暂存 (按下顺序), 松开撤销暂存;
///   - 主键按下 -> 组合生成 AHK 热键并经 <see cref="HotkeyCommitted"/> 提交, 退出捕获态;
///   - Esc 单独按下 (无任何修饰) 取消; 焦点丢失由外部调用 <see cref="Cancel"/>。
/// </summary>
public sealed class HotkeyCaptureCore
{
    private readonly List<string> _pendingPrefixes = [];

    /// <summary>是否处于捕获态。</summary>
    public bool Capturing { get; private set; }

    /// <summary>当前暂存的修饰键前缀 (按下顺序), 等待主键组成完整热键。</summary>
    public IReadOnlyList<string> PendingPrefixes => _pendingPrefixes;

    /// <summary>捕获态/暂存变化 (UI 据此刷新提示文本)。</summary>
    public event Action? StateChanged;

    /// <summary>成功组合出热键 (AHK 格式) 时提交。</summary>
    public event Action<string>? HotkeyCommitted;

    /// <summary>进入捕获态 (复刻 startCapture)。</summary>
    public void StartCapture()
    {
        Capturing = true;
        _pendingPrefixes.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>取消捕获 (复刻 Esc 取消与 onFocusOut), 未捕获时无操作。</summary>
    public void Cancel()
    {
        if (!Capturing) return;
        Capturing = false;
        _pendingPrefixes.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 处理按键按下 (复刻 onKeydown)。
    /// </summary>
    /// <param name="key">物理键。</param>
    /// <param name="anyModifierHeld">系统层面是否有修饰键处于按住状态 (Esc 单独按下才取消)。</param>
    public void HandleKeyDown(Key key, bool anyModifierHeld)
    {
        if (!Capturing) return;

        // Esc 单独按下时取消捕获 (复刻: e.key==="Escape" && 无任何修饰)
        if (key == Key.Escape && !anyModifierHeld)
        {
            Cancel();
            return;
        }

        // 修饰键: 暂存等待主键 (按住不放的重复事件靠 Contains 去重, 复刻 !e.repeat + includes)
        var mod = HotkeyLogic.ModifierFor(key);
        if (mod is not null)
        {
            if (!_pendingPrefixes.Contains(mod.Value.Prefix))
            {
                _pendingPrefixes.Add(mod.Value.Prefix);
                StateChanged?.Invoke();
            }
            return;
        }

        // 主键: 组合修饰键生成 AHK 格式热键; 不支持的键保持等待 (复刻 key==null 时忽略)
        var name = HotkeyLogic.KeyToAhkName(key);
        if (name is null) return;

        // 提交前侧别归一化: 左右修饰键统一为通配两侧的 ^/!/+/# (见 NormalizeSidePrefixes)
        var ahk = HotkeyLogic.NormalizeSidePrefixes(HotkeyLogic.BuildAhk(_pendingPrefixes, name));
        Capturing = false;
        _pendingPrefixes.Clear();
        HotkeyCommitted?.Invoke(ahk);
        StateChanged?.Invoke();
    }

    /// <summary>修饰键松开且未按主键时撤销暂存, 继续等待 (复刻 onKeyup)。</summary>
    public void HandleKeyUp(Key key)
    {
        if (!Capturing) return;
        var mod = HotkeyLogic.ModifierFor(key);
        if (mod is null) return;
        if (_pendingPrefixes.Remove(mod.Value.Prefix))
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 捕获态提示文本 (复刻 display 计算): 非捕获态显示当前热键可读形式;
    /// 捕获态显示已按修饰键 "LCtrl + ..." 或等待提示。
    /// </summary>
    public string DisplayText(string currentAhk, string waitingHint)
    {
        if (!Capturing) return HotkeyLogic.AhkToDisplay(currentAhk);
        if (_pendingPrefixes.Count > 0)
        {
            return string.Join(" + ", _pendingPrefixes.Select(p => HotkeyLogic.ModPrefixLabels[p])) + " + ...";
        }
        return waitingHint;
    }
}
