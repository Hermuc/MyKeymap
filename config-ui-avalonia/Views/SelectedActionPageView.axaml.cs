using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 选中动作列表页视图 (复刻 views/SelectedAction.vue) 的交互部分:
///   - 整卡点击进入编辑 (卡片内的开关/按钮自带命令, 点击它们不触发进入);
///   - 向页面 VM 注入真实确认对话框 (删除方案确认)。
/// </summary>
public partial class SelectedActionPageView : UserControl
{
    public SelectedActionPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => InjectConfirmDialog();
    }

    private void InjectConfirmDialog()
    {
        if (DataContext is SelectedActionPageViewModel vm && vm.ConfirmAsync is null)
        {
            vm.ConfirmAsync = ShowConfirmAsync;
        }
    }

    /// <summary>卡片整体点击进入编辑; 点在 ToggleSwitch/按钮上时交给控件自身命令。</summary>
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source
            && source.GetSelfAndVisualAncestors().Any(a => a is Button or ToggleSwitch))
        {
            return;
        }
        if (sender is Border { DataContext: SchemeCardVm card })
        {
            card.OpenCommand.Execute(null);
        }
    }

    /// <summary>两按钮确认对话框 (复刻 Vue 的确认弹窗): 返回是否确认。</summary>
    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;

        var confirmed = false;
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        var ok = new Button
        {
            Content = "OK",
            Background = new SolidColorBrush(Color.Parse("#D32F2F")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => { confirmed = true; dialog.Close(); };

        var cancel = new Button
        {
            Content = Services.I18n.T("970"),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(22, 18),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 18),
                    [DockPanel.DockProperty] = Dock.Top,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, ok },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return confirmed;
    }
}
