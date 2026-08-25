using Avalonia.Controls;
using Avalonia.Interactivity;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>总览页视图 (复刻 Home.vue 的 config_doc 展示)。</summary>
public partial class HomePageView : UserControl
{
    public HomePageView() => InitializeComponent();

    /// <summary>「编辑总览」: 弹出 markdown 编辑窗口, 保存成功后刷新渲染。</summary>
    private async void OnEditOverviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;
        // 按钮点击时本控件必已挂载到窗口, TopLevel 即宿主窗口
        var dialog = new OverviewEditWindow(vm.Main, vm.CurrentMd);
        await dialog.ShowDialog((Window)TopLevel.GetTopLevel(this)!);
        if (dialog.Saved)
        {
            await vm.ReloadAsync();
        }
    }
}
