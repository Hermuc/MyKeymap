using System.Text.Json;
using MyKeymap.Settings.Models;
// 避免与 System.Action 歧义
using Action = MyKeymap.Settings.Models.Action;

namespace MyKeymap.Settings.Tests;

/// <summary>
/// 纯单元层契约守护 (不需要真实服务):
/// 1. C# 模型的 JSON 属性名与 Go json tag 逐字对齐 (防拼写回归);
/// 2. Go omitempty 字段缺失时反序列化容忍 (可空/默认空集合);
/// 3. 只读路径默认值注入 (对照 config-ui/src/store/config.ts)。
/// </summary>
public sealed class ModelSerializationTests
{
    private static string Serialize(object value) => JsonSerializer.Serialize(value, SettingsJson.Options);

    [Fact]
    public void Config_TopLevel_JsonNames_MatchGoTags()
    {
        var json = Serialize(new Config { HelpPageHtml = "<b>x</b>" });
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "keymaps", "options", "actionSchemes", "fileGroups", "helpPageHtml", "overviewDocMd" },
            keys);
    }

    [Fact]
    public void Keymap_And_Action_JsonNames_MatchGoTags()
    {
        var km = new Keymap
        {
            Id = 1,
            Name = "主键盘",
            Enable = true,
            Hotkey = "CapsLock",
            ParentId = 0,
            Delay = 0,
            DisableAt = "notepad.exe",
            Hotkeys =
            {
                ["a"] =
                [
                    new Action
                    {
                        WindowGroupId = 1,
                        TypeId = 5,
                        Comment = "备注",
                        Hotkey = "a",
                        KeysToSend = "^c",
                        RemapToKey = "b",
                        ValueId = 2,
                        WinTitle = "ahk_exe x.exe",
                        Target = "cmd.exe",
                        Args = "/c dir",
                        WorkingDir = "C:\\",
                        RunAsAdmin = true,
                        RunInBackground = true,
                        DetectHiddenWindow = true,
                        AhkCode = "MsgBox 1",
                    },
                ],
            },
        };
        var json = Serialize(km);
        using var doc = JsonDocument.Parse(json);
        var kmKeys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "id", "name", "enable", "hotkey", "parentID", "delay", "disableAt", "hotkeys" },
            kmKeys);

        var actionKeys = doc.RootElement.GetProperty("hotkeys").GetProperty("a")[0]
            .EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "windowGroupID", "actionTypeID", "comment", "hotkey", "keysToSend", "remapToKey",
                "actionValueID", "winTitle", "target", "args", "workingDir", "runAsAdmin",
                "runInBackground", "detectHiddenWindow", "ahkCode",
            },
            actionKeys);
    }

    [Fact]
    public void Options_And_SubStructs_JsonNames_MatchGoTags()
    {
        var json = Serialize(new Options());
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "hideMatrix", "mykeymapVersion", "windowGroups", "mouse", "scroll",
                "commandInputSkin", "pathVariables", "startup", "language", "keyMapping", "keyboardLayout",
            },
            keys);

        var mouseKeys = doc.RootElement.GetProperty("mouse").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "keepMouseMode", "showTip", "tipSymbol", "delay1", "delay2",
                "fastSingle", "fastRepeat", "slowSingle", "slowRepeat",
            },
            mouseKeys);

        var scrollKeys = doc.RootElement.GetProperty("scroll").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "delay1", "delay2", "onceLineCount" }, scrollKeys);

        var skinKeys = doc.RootElement.GetProperty("commandInputSkin").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(18, skinKeys.Count);
        Assert.Contains("backgroundColor", skinKeys);
        Assert.Contains("hideAnimationDuration", skinKeys);
        Assert.Contains("windowShadowSize", skinKeys);
    }

    [Fact]
    public void ActionScheme_Rule_FileGroup_JsonNames_MatchGoTags()
    {
        var scheme = new ActionScheme
        {
            Id = 1,
            Name = "方案",
            Hotkey = "*!s",
            Enable = true,
            Rules =
            [
                new ActionRule
                {
                    Priority = 1,
                    MatchType = "fileExt",
                    MatchValue = "jpg",
                    ActionType = "open",
                    ActionValue = "x.exe",
                    WorkingDir = "C:\\",
                    Options = new RuleOptions { CopyToClipboard = true, ClearSelection = true, Confirm = true },
                },
            ],
        };
        var json = Serialize(scheme);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            new HashSet<string> { "id", "name", "hotkey", "enable", "rules" },
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet());
        var rule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal(
            new HashSet<string> { "priority", "matchType", "matchValue", "actionType", "actionValue", "workingDir", "options" },
            rule.EnumerateObject().Select(p => p.Name).ToHashSet());
        Assert.Equal(
            new HashSet<string> { "copyToClipboard", "clearSelection", "confirm" },
            rule.GetProperty("options").EnumerateObject().Select(p => p.Name).ToHashSet());

        var fgJson = Serialize(new FileGroup { Name = "image", Label = "图片", Exts = ["jpg"] });
        using var fgDoc = JsonDocument.Parse(fgJson);
        Assert.Equal(
            new HashSet<string> { "name", "label", "exts" },
            fgDoc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet());

        var wgJson = Serialize(new WindowGroup { Id = 1, Name = "g", Value = "v", ConditionType = 1 });
        using var wgDoc = JsonDocument.Parse(wgJson);
        Assert.Equal(
            new HashSet<string> { "id", "name", "value", "conditionType" },
            wgDoc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet());
    }

    [Fact]
    public void Deserialize_ToleratesMissingOmitemptyFields()
    {
        // 模拟旧版 config.json: 缺 actionSchemes / fileGroups / helpPageHtml, Action 仅带必需字段
        const string minimal = """
        {
          "keymaps": [
            { "id": 1, "name": "主", "enable": true, "hotkey": "CapsLock", "parentID": 0, "delay": 0,
              "disableAt": "", "hotkeys": { "a": [ { "windowGroupID": 0, "actionTypeID": 5 } ] } }
          ],
          "options": {
            "hideMatrix": false, "mykeymapVersion": "", "windowGroups": [],
            "mouse": {}, "scroll": {}, "commandInputSkin": {}, "pathVariables": [],
            "startup": false, "language": "", "keyMapping": "", "keyboardLayout": ""
          }
        }
        """;
        var config = JsonSerializer.Deserialize<Config>(minimal, SettingsJson.Options);
        Assert.NotNull(config);
        Assert.Empty(config!.ActionSchemes);
        Assert.Empty(config.FileGroups);
        Assert.Equal("", config.HelpPageHtml);
        Assert.Single(config.Keymaps);
        var action = config.Keymaps[0].Hotkeys["a"][0];
        Assert.Equal(5, action.TypeId);
        Assert.Equal("", action.Comment);
        Assert.False(action.RunAsAdmin);
    }
}

public sealed class ConfigReadDefaultsTests
{
    private static Config EmptyConfig() => new();

    [Fact]
    public void Apply_FillsKeyboardLayout_WhenMissing()
    {
        var config = EmptyConfig();
        ConfigReadDefaults.Apply(config);
        Assert.Equal(ConfigReadDefaults.DefaultKeyboardLayout, config.Options.KeyboardLayout);

        // 已有值时不覆盖
        config.Options.KeyboardLayout = "custom layout";
        ConfigReadDefaults.Apply(config);
        Assert.Equal("custom layout", config.Options.KeyboardLayout);
    }

    [Fact]
    public void Apply_FillsLanguage_WithZhOrEn()
    {
        var config = EmptyConfig();
        ConfigReadDefaults.Apply(config);
        Assert.Contains(config.Options.Language, new[] { "zh", "en" });

        config.Options.Language = "zh";
        ConfigReadDefaults.Apply(config);
        Assert.Equal("zh", config.Options.Language);
    }

    [Fact]
    public void Apply_InsertsExcludeWindowGroup_WhenAbsent()
    {
        var config = EmptyConfig();
        config.Options.WindowGroups.Add(new WindowGroup { Id = 1, Name = "浏览器" });

        ConfigReadDefaults.Apply(config);

        Assert.Equal(2, config.Options.WindowGroups.Count);
        Assert.Equal(ConfigReadDefaults.ExcludeGroupId, config.Options.WindowGroups[0].Id);
        Assert.Equal("🚫 Exclude", config.Options.WindowGroups[0].Name);

        // 已存在排除项时不重复插入
        ConfigReadDefaults.Apply(config);
        Assert.Equal(2, config.Options.WindowGroups.Count);
    }

    [Fact]
    public void Apply_ActionSchemes_NeverNull()
    {
        var config = EmptyConfig();
        config.ActionSchemes = null!;
        ConfigReadDefaults.Apply(config);
        Assert.NotNull(config.ActionSchemes);
        Assert.Empty(config.ActionSchemes);
    }
}
