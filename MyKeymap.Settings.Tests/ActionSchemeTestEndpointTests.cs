using MyKeymap.Settings.Models;
using MyKeymap.Settings.Services;
using MyKeymap.Settings.Tests.Infrastructure;

namespace MyKeymap.Settings.Tests;

/// <summary>
/// POST /api/action-schemes/test 契约:
/// 快照优先 (编辑中未保存方案) / 磁盘回退 (仅 schemeId) / isFile 双语义 / 404 / 400。
/// </summary>
public sealed class ActionSchemeTestEndpointTests : ServerTestBase
{
    [Fact]
    public async Task Test_DiskFallback_FileContent_MatchesFileExtRule()
    {
        // 先落盘一个方案, 然后仅凭 schemeId 测试 (回退磁盘语义)
        var created = await Client.CreateActionSchemeAsync(TestData.ValidScheme());
        Assert.True(created.Success, created.ErrorMessage);

        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            SchemeId = created.Value!.Id,
            Content = @"C:\图片\照片.jpg",
            IsFile = true,
        });
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.True(resp.Value!.Matched);
        Assert.Equal("fileExt", resp.Value.Rule!.MatchType);
        Assert.False(string.IsNullOrEmpty(resp.Value.Preview), "命中时应返回执行预览");
        Assert.Contains("照片.jpg", resp.Value.Preview);
    }

    [Fact]
    public async Task Test_DiskFallback_NoRuleMatches_ReturnsMatchedFalse()
    {
        var created = await Client.CreateActionSchemeAsync(TestData.FileExtOnlyScheme());
        Assert.True(created.Success, created.ErrorMessage);

        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            SchemeId = created.Value!.Id,
            Content = @"C:\文档\readme.pdf",
            IsFile = true,
        });
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.Equal(200, resp.StatusCode);
        Assert.False(resp.Value!.Matched);
        Assert.Null(resp.Value.Rule);
    }

    [Fact]
    public async Task Test_Snapshot_TakesPriority_WithoutPersisting()
    {
        var before = await Client.GetActionSchemesAsync();
        Assert.True(before.Success);
        var countBefore = before.Value!.Count;

        // schemeId 指向不存在的方案 + 携带编辑中快照: 快照必须优先且不落盘
        var snapshot = new ActionScheme
        {
            Name = "编辑中的草稿",
            Hotkey = "*!e",
            Enable = true,
            Rules =
            [
                new ActionRule
                {
                    Priority = 1,
                    MatchType = "textType",
                    MatchValue = "url",
                    ActionType = "open_url",
                    ActionValue = "",
                },
            ],
        };
        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            SchemeId = 999999,
            Content = "https://example.com/page",
            IsFile = false,
            Scheme = snapshot,
        });
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.True(resp.Value!.Matched);
        Assert.Equal("textType", resp.Value.Rule!.MatchType);
        Assert.StartsWith("用默认浏览器打开: ", resp.Value.Preview);
        Assert.Contains("https://example.com/page", resp.Value.Preview);

        // 快照不应被持久化
        var after = await Client.GetActionSchemesAsync();
        Assert.Equal(countBefore, after.Value!.Count);
    }

    [Fact]
    public async Task Test_TextContent_MatchesTextTypePlain()
    {
        var snapshot = new ActionScheme
        {
            Name = "纯文本草稿",
            Hotkey = "*!p",
            Enable = true,
            Rules =
            [
                new ActionRule
                {
                    Priority = 1,
                    MatchType = "textType",
                    MatchValue = "plain",
                    ActionType = "search",
                    ActionValue = "https://www.bing.com/search?q=%selected%",
                },
            ],
        };
        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            Content = "hello world",
            IsFile = false,
            Scheme = snapshot,
        });
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.True(resp.Value!.Matched);
        // search 预览: %selected% 替换为 URL 编码后的内容 (空格 -> %20)
        Assert.Equal("https://www.bing.com/search?q=hello%20world", resp.Value.Preview);
    }

    [Fact]
    public async Task Test_FileExtRule_DoesNotMatchTextContent()
    {
        // isFile=false 时, fileExt 规则必须不命中 (matchActionRule 的 isFile 分支语义)
        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            Content = "a.jpg",
            IsFile = false,
            Scheme = TestData.FileExtOnlyScheme(),
        });
        Assert.True(resp.Success, resp.ErrorMessage);
        Assert.False(resp.Value!.Matched);
    }

    [Fact]
    public async Task Test_UnknownSchemeId_WithoutSnapshot_Returns404()
    {
        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            SchemeId = 424242,
            Content = "anything",
            IsFile = false,
        });
        Assert.False(resp.Success);
        Assert.Equal(404, resp.StatusCode);
        Assert.Equal("scheme not found", resp.ErrorMessage);
    }

    [Fact]
    public async Task Test_InvalidComboSnapshot_Returns400()
    {
        var resp = await Client.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            Content = "https://example.com",
            IsFile = false,
            Scheme = TestData.InvalidTextTypeComboScheme(),
        });
        Assert.False(resp.Success);
        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("不匹配", resp.ErrorMessage);
    }
}
