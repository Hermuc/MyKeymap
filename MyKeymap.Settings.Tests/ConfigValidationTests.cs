using System.Security.Cryptography;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.Tests.Infrastructure;

namespace MyKeymap.Settings.Tests;

/// <summary>
/// PUT /config 的 400 校验契约 (对照 Go ValidateActionSchemeRules / ValidateFileGroups)。
/// 要点: 校验失败必须拒绝写盘 (配置文件保持不变), message 以「保存失败」开头。
/// </summary>
public sealed class ConfigValidationTests : ServerTestBase
{
    private static string Sha256Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    [Fact]
    public async Task Put_InvalidTextTypeCombo_Returns400_WithMessage_AndFileUnchanged()
    {
        var before = Sha256Of(Server.ConfigPath);

        var get = await Client.GetConfigAsync();
        Assert.True(get.Success, get.ErrorMessage);

        // textType(url) + open_registry 是 Go textTypeActions 词表禁止的组合
        get.Value!.ActionSchemes.Add(TestData.InvalidTextTypeComboScheme());

        var put = await Client.SaveConfigAsync(get.Value);
        Assert.False(put.Success, "非法组合不应保存成功");
        Assert.Equal(400, put.StatusCode);
        Assert.Contains("保存失败", put.ErrorMessage);
        Assert.Contains("不匹配", put.ErrorMessage);

        Assert.Equal(before, Sha256Of(Server.ConfigPath));
    }

    [Fact]
    public async Task Put_UnknownTextFeature_Returns400()
    {
        var get = await Client.GetConfigAsync();
        Assert.True(get.Success, get.ErrorMessage);
        get.Value!.ActionSchemes.Add(TestData.UnknownTextFeatureScheme());

        var put = await Client.SaveConfigAsync(get.Value);
        Assert.False(put.Success);
        Assert.Equal(400, put.StatusCode);
        Assert.Contains("保存失败", put.ErrorMessage);
        Assert.Contains("未知的文本特征", put.ErrorMessage);
    }

    [Fact]
    public async Task Put_FileGroupMissingName_Returns400()
    {
        var get = await Client.GetConfigAsync();
        Assert.True(get.Success, get.ErrorMessage);
        get.Value!.FileGroups.Add(new FileGroup { Name = "  ", Label = "坏分组", Exts = ["txt"] });

        var put = await Client.SaveConfigAsync(get.Value);
        Assert.False(put.Success);
        Assert.Equal(400, put.StatusCode);
        Assert.Contains("保存失败", put.ErrorMessage);
        Assert.Contains("文件分组", put.ErrorMessage);
        Assert.Contains("名称", put.ErrorMessage);
    }

    [Fact]
    public async Task Put_FileGroupEmptyExts_Returns400()
    {
        var get = await Client.GetConfigAsync();
        Assert.True(get.Success, get.ErrorMessage);
        get.Value!.FileGroups.Add(new FileGroup { Name = "empty", Label = "空后缀", Exts = [] });

        var put = await Client.SaveConfigAsync(get.Value);
        Assert.False(put.Success);
        Assert.Equal(400, put.StatusCode);
        Assert.Contains("保存失败", put.ErrorMessage);
        Assert.Contains("后缀列表", put.ErrorMessage);
    }
}
