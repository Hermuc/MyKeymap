using Avalonia.Controls;
using Avalonia.Input;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>缩写命令页视图 (复刻 views/Abbr.vue): 命令框回车执行指令。</summary>
public partial class AbbrPageView : UserControl
{
    public AbbrPageView() => InitializeComponent();

    /// <summary>命令框回车 (复刻 @keydown.enter=runCmd)。</summary>
    private void OnCmdKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AbbrPageViewModel vm)
        {
            vm.RunCmdCommand.Execute(null);
            e.Handled = true;
        }
    }
}
