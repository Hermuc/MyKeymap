using System.Text.Json;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.Tests.Infrastructure;

namespace MyKeymap.Settings.Tests;

/// <summary>共享测试数据构造器 (合法/非法方案)。</summary>
internal static class TestData
{
    /// <summary>合法方案: fileExt(jpg,png)->open, textType(url)->open_url, fileExt(*)->copy。</summary>
    public static ActionScheme ValidScheme(string name = "契约测试方案") => new()
    {
        Name = name,
        Hotkey = "*!s",
        Enable = true,
        Rules =
        [
            new ActionRule
            {
                Priority = 1,
                MatchType = "fileExt",
                MatchValue = "jpg,png",
                ActionType = "open",
                // 含 %selected% 占位符, 便于在 /test 预览断言替换结果 (PreviewAction 默认分支)
                ActionValue = "notepad.exe \"%selected%\"",
            },
            new ActionRule
            {
                Priority = 2,
                MatchType = "textType",
                MatchValue = "url",
                ActionType = "open_url",
                ActionValue = "",
            },
            new ActionRule
            {
                Priority = 3,
                MatchType = "fileExt",
                MatchValue = "*",
                ActionType = "copy",
                ActionValue = "",
                Options = new RuleOptions { CopyToClipboard = true },
            },
        ],
    };

    /// <summary>非法方案: textType(url) 配 open_registry (特征与行为语义不匹配, Go 词表禁止)。</summary>
    public static ActionScheme InvalidTextTypeComboScheme(string name = "非法组合方案") => new()
    {
        Name = name,
        Hotkey = "*!x",
        Enable = true,
        Rules =
        [
            new ActionRule
            {
                Priority = 1,
                MatchType = "textType",
                MatchValue = "url",
                ActionType = "open_registry",
                ActionValue = "",
            },
        ],
    };

    /// <summary>非法方案: 未知的文本特征值 (词表仅 url/path/magnet/plain)。</summary>
    public static ActionScheme UnknownTextFeatureScheme() => new()
    {
        Name = "未知特征方案",
        Hotkey = "*!y",
        Enable = true,
        Rules =
        [
            new ActionRule
            {
                Priority = 1,
                MatchType = "textType",
                MatchValue = "regex",
                ActionType = "search",
                ActionValue = "https://www.bing.com/search?q=%selected%",
            },
        ],
    };

    /// <summary>仅 fileExt 规则的方案 (验证 isFile 语义用)。</summary>
    public static ActionScheme FileExtOnlyScheme() => new()
    {
        Name = "仅文件后缀方案",
        Hotkey = "*!f",
        Enable = true,
        Rules =
        [
            new ActionRule
            {
                Priority = 1,
                MatchType = "fileExt",
                MatchValue = "jpg",
                ActionType = "open",
                ActionValue = "notepad.exe",
            },
        ],
    };
}

/// <summary>
/// GET /config 结构契约 + PUT 往返语义 + 字节级落盘格式守护。
/// 对照: types.go 全部结构体与 json tag; SaveConfigFile 的写盘约定。
/// </summary>
public sealed class ConfigContractTests : ServerTestBase
{
    [Fact]
    public async Task GetConfig_Structure_MatchesGoContract()
    {
        var resp = await Client.GetConfigAsync();
        Assert.True(resp.Success, $"GET /config 失败: {resp.ErrorMessage}");

        using var doc = JsonDocument.Parse(resp.RawBody);
        var root = doc.RootElement;

        // 顶层字段 (对照 Go struct Config)
        Assert.Equal(JsonValueKind.Array, root.GetProperty("keymaps").ValueKind);
        Assert.True(root.GetProperty("keymaps").GetArrayLength() > 0, "种子配置应有 keymaps");
        Assert.Equal(JsonValueKind.Object, root.GetProperty("options").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("actionSchemes").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("fileGroups").ValueKind);

        // Keymap 字段 (对照 Go struct Keymap)
        var km = root.GetProperty("keymaps")[0];
        foreach (var prop in new[] { "id", "name", "enable", "hotkey", "parentID", "delay", "disableAt", "hotkeys" })
        {
            Assert.True(km.TryGetProperty(prop, out _), $"keymaps[0] 缺少字段 {prop}");
        }

        // Action 字段: json tag 是 actionTypeID / windowGroupID / actionValueID (非常规命名, 重点核对)
        var hotkeys = km.GetProperty("hotkeys");
        Assert.True(hotkeys.EnumerateObject().Any(), "hotkeys 映射不应为空");
        var firstActions = hotkeys.EnumerateObject().First().Value;
        Assert.True(firstActions.GetArrayLength() > 0);
        var action = firstActions[0];
        Assert.True(action.TryGetProperty("actionTypeID", out _), "Action 缺少 actionTypeID");
        Assert.True(action.TryGetProperty("windowGroupID", out _), "Action 缺少 windowGroupID");

        // Options 字段 (对照 Go struct Options)
        var options = root.GetProperty("options");
        foreach (var prop in new[]
        {
            "hideMatrix", "mykeymapVersion", "windowGroups", "mouse", "scroll",
            "commandInputSkin", "pathVariables", "startup", "language", "keyMapping", "keyboardLayout",
        })
        {
            Assert.True(options.TryGetProperty(prop, out _), $"options 缺少字段 {prop}");
        }
        // Go ParseConfig 读路径注入: 版本号来自构建期 ldflags, 非空
        Assert.False(string.IsNullOrEmpty(options.GetProperty("mykeymapVersion").GetString()),
            "options.mykeymapVersion 应由 Go 侧注入构建版本号");
        // tipSymbol 缺省注入 🐶 (种子配置若已带值则仅断言非空)
        Assert.False(string.IsNullOrEmpty(options.GetProperty("mouse").GetProperty("tipSymbol").GetString()));
        // commandInputSkin 全零时 Go 注入默认皮肤
        Assert.False(string.IsNullOrEmpty(options.GetProperty("commandInputSkin").GetProperty("backgroundColor").GetString()));

        // FileGroup 字段 (出厂默认配置可为空数组; 非空时核对字段结构)
        var fgs = root.GetProperty("fileGroups");
        Assert.Equal(JsonValueKind.Array, fgs.ValueKind);
        if (fgs.GetArrayLength() > 0)
        {
            var fg = fgs[0];
            Assert.True(fg.TryGetProperty("name", out _));
            Assert.True(fg.TryGetProperty("label", out _));
            Assert.True(fg.TryGetProperty("exts", out _));
        }

        // 强类型反序列化抽查
        var config = resp.Value!;
        Assert.Equal(root.GetProperty("keymaps").GetArrayLength(), config.Keymaps.Count);
        Assert.Equal(root.GetProperty("fileGroups").GetArrayLength(), config.FileGroups.Count);
        Assert.Equal(
            options.GetProperty("keyboardLayout").GetString(),
            config.Options.KeyboardLayout);
    }

    [Fact]
    public async Task PutRoundTrip_GetPutGet_SemanticallyEqual()
    {
        var first = await Client.GetConfigAsync();
        Assert.True(first.Success, first.ErrorMessage);

        // 原样 PUT 回去
        var put = await Client.SaveConfigAsync(first.Value!);
        Assert.True(put.Success, $"PUT /config 失败: {put.ErrorMessage}");
        Assert.Equal("ok", put.Value?.Message);

        var second = await Client.GetConfigAsync();
        Assert.True(second.Success, second.ErrorMessage);

        // 语义相等: 两侧都经同一 C# 序列化归一化后深比较 (容忍 Go omitempty 省略零值字段)
        var a = JsonSerializer.SerializeToElement(first.Value, SettingsJson.Options);
        var b = JsonSerializer.SerializeToElement(second.Value, SettingsJson.Options);
        Assert.True(JsonElement.DeepEquals(a, b),
            $"PUT 往返后配置语义发生变化:\nA={a.GetRawText().AsSpan(0, Math.Min(400, a.GetRawText().Length))}\nB={b.GetRawText().AsSpan(0, Math.Min(400, b.GetRawText().Length))}");
    }

    [Fact]
    public async Task Put_ChangedField_HideMatrix_TakesEffect()
    {
        var first = await Client.GetConfigAsync();
        Assert.True(first.Success, first.ErrorMessage);
        var original = first.Value!.Options.HideMatrix;

        first.Value.Options.HideMatrix = !original;
        var put = await Client.SaveConfigAsync(first.Value);
        Assert.True(put.Success, put.ErrorMessage);

        var second = await Client.GetConfigAsync();
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(!original, second.Value!.Options.HideMatrix);
    }

    [Fact]
    public async Task Put_ConfigFile_ByteFormat_NoBom_TwoSpaceIndent_SingleTrailingNewline_NoUnicodeEscapes()
    {
        // 由一次合法 PUT 触发 Go 写盘, 核验 SaveConfigFile 的字节级格式约定 (守护网)
        var first = await Client.GetConfigAsync();
        Assert.True(first.Success, first.ErrorMessage);
        var put = await Client.SaveConfigAsync(first.Value!);
        Assert.True(put.Success, put.ErrorMessage);

        var bytes = await File.ReadAllBytesAsync(Server.ConfigPath);
        Assert.True(bytes.Length > 0, "config.json 不应为空");

        // 1. 无 BOM
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "config.json 不应带 UTF-8 BOM");

        // 2. 结尾单个 \n (Go json.Encoder.Encode 追加一个换行)
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);

        var text = System.Text.Encoding.UTF8.GetString(bytes);

        // 3. 2 空格缩进: 顶层键恰好 2 空格, 所有行缩进均为偶数空格且不含 \t
        var lines = text.Split('\n');
        var topLevelLine = lines.FirstOrDefault(l => l.StartsWith("  \"keymaps\"") || l.StartsWith("  \"options\""));
        Assert.True(topLevelLine is not null, "应存在 2 空格缩进的顶层键");
        Assert.DoesNotContain("\t", topLevelLine!);
        Assert.StartsWith("  \"", topLevelLine!);
        Assert.False(topLevelLine!.StartsWith("   "), "顶层键缩进应恰好为 2 空格");
        foreach (var line in lines)
        {
            var indent = line.Length - line.TrimStart(' ').Length;
            Assert.True(indent % 2 == 0, $"存在奇数空格缩进行: '{line}'");
        }

        // 4. 中文不被 \u 转义 (Go SetEscapeHTML(false) + 默认不转义非 ASCII)
        Assert.Matches("[\u4e00-\u9fff]", text);
        Assert.DoesNotMatch(@"\\u[0-9a-fA-F]{4}", text);
    }
}
