using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using MyKeymap.Settings.Services;

namespace MyKeymap.Settings.ViewModels;

/// <summary>
/// 总览页 (复刻 Home.vue): Vue 版是 &lt;iframe src="/config_doc.html"&gt; 展示帮助文档。
/// Avalonia 无内嵌浏览器, 等价实现为: 从后端静态站点拉取 config_doc.html,
/// 提取正文文本完整展示 (保留信息完整性); 拉取失败时回退到内置快速上览引导。
/// </summary>
public sealed partial class HomePageViewModel : ObservableObject
{
    private readonly BackendSession _session;

    public HomePageViewModel(BackendSession session) => _session = session;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>true = 成功拉取到 config_doc.html 正文。</summary>
    [ObservableProperty]
    private bool _docAvailable;

    /// <summary>config_doc.html 提取出的正文文本。</summary>
    [ObservableProperty]
    private string _docText = "";

    /// <summary>语言切换递增, 驱动静态文案绑定重算。</summary>
    [ObservableProperty]
    private int _languageTick;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var html = await _session.GetRawTextAsync("/config_doc.html", ct);
            if (!string.IsNullOrWhiteSpace(html))
            {
                DocText = HtmlToText(html);
                DocAvailable = true;
            }
            else
            {
                DocAvailable = false;
            }
        }
        catch (Exception)
        {
            DocAvailable = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>极简 HTML -> 文本: 去 style/script/标签, 解码常见实体, 压缩空行。</summary>
    internal static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/h[1-6]|/li|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = text
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&amp;", "&");
        // 压缩连续空行
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }
}
