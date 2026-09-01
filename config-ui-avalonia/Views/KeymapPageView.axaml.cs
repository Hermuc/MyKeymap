using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace MyKeymap.Settings.Views;

/// <summary>键位图页视图 (复刻 views/Keymap.vue)。</summary>
public partial class KeymapPageView : UserControl
{
    public KeymapPageView() => InitializeComponent();

    /// <summary>左列目标占位: 面板固定 620 + 右补偿 33 = 653 的原始值, 后经微调 653→630→616,
    /// 与标准 10 键最宽行宽等宽 (右补偿恒 0); 备注列↔网格间距改由备注列左 Margin 32 (对齐 Command 页) 控制。</summary>
    private const double LeftColumnWidth = 616;

    /// <summary>
    /// 键盘行面板尺寸变化 -> 编辑面板宽度同步: 遍历 KeyboardGrid 下各行面板
    /// (横向且 Left 对齐, Bounds.Width 即该行实际自然宽度), 取最大值赋给编辑面板外框,
    /// 使外框与键盘网格最宽一行严格等宽 (内部白卡片由外框 Padding=2,0 左右各内缩 2);
    /// 不同布局/模式下最宽行不同, 故不写死数值。
    /// 右补偿 Margin 同时按 616-W 重算: 左列占位固定 616, 备注列与网格间距较原值减半;
    /// W>616 时补偿取 0 (占位只能增不能负, 备注列向右顺延)。顶间距收窄为 12。
    /// 行面板位于 ItemsPanelTemplate 内 (模板级 NameScope, 页面 code-behind 无法以 x:Name 引用),
    /// 故在 XAML 中直接挂事件; XAML 中 Width=620 仅作首帧初始值。
    /// </summary>
    private void OnKeyboardRowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var max = 0.0;
        foreach (var panel in KeyboardGrid.GetVisualDescendants().OfType<StackPanel>())
        {
            if (panel.Orientation == Orientation.Horizontal)
                max = Math.Max(max, panel.Bounds.Width);
        }
        if (max <= 0) return;

        EditorFrame.Width = max;
        var rightComp = Math.Max(0, LeftColumnWidth - max);
        EditorFrame.Margin = new Thickness(0, 12, rightComp, 0);
    }
}
