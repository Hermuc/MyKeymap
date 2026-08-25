using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MyKeymap.Settings.Services;

// ============================================================================
// MarkdownRenderer: 总览页文档渲染层 (块模型 -> Avalonia 控件)
//
// 消费 MarkdownParser 的不可变块模型, 构建展示控件树。
// 外部依赖全部经回调注入, 不感知会话/网络细节:
//   - loadImage: 图片块异步加载 (调用方负责从后端拉取, 失败返回 null 保持空白)
//   - openLink:  链接点击策略 (调用方决定如何打开外部/内部链接)
// ============================================================================
public static class MarkdownRenderer
{
    private const string LinkColor = "#4169E1";
    private const string CodeColor = "#C7254E";
    private const string CodeFont = "Consolas";

    /// <summary>标题字号 (按级别): # 24 / ## 22 / ### 18 / #### 16。</summary>
    private static readonly int[] HeadingSizes = [24, 22, 18, 16];

    /// <summary>无序列表符号 (按层级): • / ◦ / ▪。</summary>
    private static readonly string[] BulletChars = ["•", "◦", "▪"];

    /// <summary>
    /// 渲染整篇文档。返回按文档顺序排列的控件 (标题/列表/图片/段落)。
    /// </summary>
    public static List<Control> Render(IReadOnlyList<MdBlock> blocks, Func<string, Task<byte[]?>> loadImage, Action<string> openLink)
    {
        var controls = new List<Control>(blocks.Count);
        foreach (var block in blocks)
        {
            controls.Add(block switch
            {
                MdHeading h => BuildHeading(h, openLink),
                MdParagraph p => BuildParagraph(p, openLink),
                MdList l => BuildList(l, openLink),
                MdImage img => BuildImage(img, loadImage),
                _ => new TextBlock { Text = "" },
            });
        }
        return controls;
    }

    /// <summary>构建标题块: 加粗 + 分级字号 + 段前留白。</summary>
    private static Control BuildHeading(MdHeading heading, Action<string> openLink)
    {
        var size = HeadingSizes[Math.Clamp(heading.Level, 1, 4) - 1];
        var tb = new TextBlock
        {
            FontSize = size,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 14, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        AppendInline(tb, MarkdownParser.ParseInline(heading.Text), size, openLink);
        return tb;
    }

    /// <summary>构建段落: 14px 正文, 1.7 倍行高 (与旧版 config_doc 正文观感一致)。</summary>
    private static Control BuildParagraph(MdParagraph paragraph, Action<string> openLink)
    {
        var tb = new TextBlock
        {
            FontSize = 14,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
        };
        AppendInline(tb, paragraph.Inlines, 14, openLink);
        return tb;
    }

    /// <summary>构建列表块: 每项按层级缩进, 有序沿用原文编号, 无序用层级符号。</summary>
    private static Control BuildList(MdList list, Action<string> openLink)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var item in list.Items)
        {
            var tb = new TextBlock
            {
                FontSize = 14,
                LineHeight = 23,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20 * (item.Level - 1), 0, 0, 0),
            };
            var bullet = item.Ordered ? item.Ordinal + "." : BulletChars[Math.Clamp(item.Level, 1, 3) - 1];
            tb.Inlines.Add(new Run(bullet + " ")
            {
                Foreground = new SolidColorBrush(Color.Parse(LinkColor)),
                FontWeight = FontWeight.SemiBold,
            });
            AppendInline(tb, item.Inlines, 14, openLink);
            panel.Children.Add(tb);
        }
        return panel;
    }

    /// <summary>构建图片块: 异步加载, 最大宽度 680, 等比缩放, 失败保持空白。</summary>
    private static Control BuildImage(MdImage image, Func<string, Task<byte[]?>> loadImage)
    {
        var img = new Image
        {
            MaxWidth = 680,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 8),
        };
        _ = LoadImageAsync(img, image.Src, loadImage);
        return img;
    }

    private static async Task LoadImageAsync(Image image, string src, Func<string, Task<byte[]?>> loadImage)
    {
        try
        {
            var bytes = await loadImage(src);
            if (bytes is null || bytes.Length == 0) return;
            using var ms = new MemoryStream(bytes);
            image.Source = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch
        {
            // 图片加载失败: 保持空白占位, 不影响文档其余部分
        }
    }

    /// <summary>行内渲染: 按片段种类追加 Run / 代码 Run / 链接控件。</summary>
    private static void AppendInline(TextBlock tb, IReadOnlyList<MdInline> inlines, int fontSize, Action<string> openLink)
    {
        foreach (var inline in inlines)
        {
            switch (inline.Kind)
            {
                case MdInlineKind.Code:
                    tb.Inlines.Add(new Run(inline.Text)
                    {
                        FontFamily = new FontFamily(CodeFont),
                        FontSize = fontSize - 1,
                        Foreground = new SolidColorBrush(Color.Parse(CodeColor)),
                    });
                    break;
                case MdInlineKind.Link:
                    tb.Inlines.Add(new InlineUIContainer { Child = BuildLink(inline.Text, inline.Url, fontSize, openLink) });
                    break;
                default:
                    tb.Inlines.Add(new Run(inline.Text) { FontSize = fontSize });
                    break;
            }
        }
    }

    /// <summary>链接控件: 蓝色下划线 + 手型光标, 点击回调 openLink。</summary>
    private static TextBlock BuildLink(string text, string url, int fontSize, Action<string> openLink)
    {
        var link = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.Parse(LinkColor)),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        link.PointerPressed += (_, _) => openLink(url);
        return link;
    }
}
