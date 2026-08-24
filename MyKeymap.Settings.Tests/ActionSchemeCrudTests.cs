using MyKeymap.Settings.Models;
using MyKeymap.Settings.Tests.Infrastructure;

namespace MyKeymap.Settings.Tests;

/// <summary>
/// /api/action-schemes CRUD 全流程契约。
/// 对照: cmd/settings/actionscheme.go —— 创建分配 max(id)+1、更新、单查、删除、404 语义。
/// </summary>
public sealed class ActionSchemeCrudTests : ServerTestBase
{
    [Fact]
    public async Task GetActionSchemes_ReturnsArray_FromSeedConfig()
    {
        var resp = await Client.GetActionSchemesAsync();
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.NotNull(resp.Value);
        // 种子 data\config.json 自带 1 个方案; 空时后端也保证返回 [] 而非 null
        Assert.True(resp.Value!.Count >= 1, "种子配置应至少含 1 个选中动作方案");
        Assert.All(resp.Value, s => Assert.True(s.Id > 0, "方案 id 应为正数"));
    }

    [Fact]
    public async Task Create_AssignsMaxIdPlusOne_AndPersists()
    {
        var baseline = await Client.GetActionSchemesAsync();
        Assert.True(baseline.Success, baseline.ErrorMessage);
        var maxId = baseline.Value!.Count == 0 ? 0 : baseline.Value.Max(s => s.Id);

        var scheme = TestData.ValidScheme();
        var created = await Client.CreateActionSchemeAsync(scheme);
        Assert.True(created.Success, $"POST /api/action-schemes 失败: {created.ErrorMessage}");
        Assert.Equal(maxId + 1, created.Value!.Id);
        Assert.Equal(scheme.Name, created.Value.Name);

        // 列表可见
        var list = await Client.GetActionSchemesAsync();
        Assert.True(list.Success);
        Assert.Contains(list.Value!, s => s.Id == created.Value.Id && s.Name == scheme.Name);

        // 单查一致
        var single = await Client.GetActionSchemeAsync(created.Value.Id);
        Assert.True(single.Success, single.ErrorMessage);
        Assert.Equal(scheme.Name, single.Value!.Name);
        Assert.Equal(scheme.Hotkey, single.Value.Hotkey);
        Assert.Equal(scheme.Rules.Count, single.Value.Rules.Count);
        Assert.Equal("fileExt", single.Value.Rules[0].MatchType);
        Assert.Equal("jpg,png", single.Value.Rules[0].MatchValue);
    }

    [Fact]
    public async Task Update_ChangesFields_AndKeepsId()
    {
        var created = await Client.CreateActionSchemeAsync(TestData.ValidScheme());
        Assert.True(created.Success, created.ErrorMessage);
        var id = created.Value!.Id;

        var updated = TestData.ValidScheme(name: "改名后的方案");
        updated.Hotkey = "*!d";
        var put = await Client.UpdateActionSchemeAsync(id, updated);
        Assert.True(put.Success, put.ErrorMessage);
        Assert.Equal(id, put.Value!.Id);

        var single = await Client.GetActionSchemeAsync(id);
        Assert.True(single.Success);
        Assert.Equal("改名后的方案", single.Value!.Name);
        Assert.Equal("*!d", single.Value.Hotkey);
    }

    [Fact]
    public async Task Update_NonexistentId_Returns404()
    {
        var put = await Client.UpdateActionSchemeAsync(999999, TestData.ValidScheme());
        Assert.False(put.Success);
        Assert.Equal(404, put.StatusCode);
        Assert.Equal("scheme not found", put.ErrorMessage);
    }

    [Fact]
    public async Task Get_NonexistentId_Returns404_WithMessage()
    {
        var resp = await Client.GetActionSchemeAsync(999999);
        Assert.False(resp.Success);
        Assert.Equal(404, resp.StatusCode);
        Assert.Equal("scheme not found", resp.ErrorMessage);
    }

    [Fact]
    public async Task Create_InvalidRules_Returns400()
    {
        var resp = await Client.CreateActionSchemeAsync(TestData.InvalidTextTypeComboScheme());
        Assert.False(resp.Success);
        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("不匹配", resp.ErrorMessage);
        // 契约细节: 方案级 API 的 400 message 无「保存失败」前缀 (仅 PUT /config 有)
        Assert.DoesNotContain("保存失败", resp.ErrorMessage);
    }

    [Fact]
    public async Task Delete_RemovesScheme_SecondDeleteAndQueryReturn404()
    {
        var created = await Client.CreateActionSchemeAsync(TestData.ValidScheme());
        Assert.True(created.Success, created.ErrorMessage);
        var id = created.Value!.Id;

        var del = await Client.DeleteActionSchemeAsync(id);
        Assert.True(del.Success, del.ErrorMessage);
        Assert.Equal(200, del.StatusCode);
        Assert.Equal("ok", del.Value?.Message);

        var gone = await Client.GetActionSchemeAsync(id);
        Assert.Equal(404, gone.StatusCode);

        var delAgain = await Client.DeleteActionSchemeAsync(id);
        Assert.False(delAgain.Success);
        Assert.Equal(404, delAgain.StatusCode);
        Assert.Equal("scheme not found", delAgain.ErrorMessage);

        var list = await Client.GetActionSchemesAsync();
        Assert.DoesNotContain(list.Value!, s => s.Id == id);
    }
}
