using Avalonia.Controls;
using Avalonia.Interactivity;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 行为库窗口 (CONTRACTS §3.9): 浏览/新建/编辑/删除行为包; 新建与编辑打开
/// <see cref="BehaviorEditWindow"/> 模态表单; 变更标记 IsDirty, 「立即生效」显式重启引擎。
/// </summary>
public partial class BehaviorLibraryWindow : Window
{
    private readonly BehaviorLibraryViewModel _vm;

    public BehaviorLibraryWindow()
    {
        InitializeComponent();
        _vm = (BehaviorLibraryViewModel)DataContext!;
        _ = ReloadAsync();
        Closed += (_, _) => _vm.UnsubscribeLanguage();
    }

    private async Task ReloadAsync()
    {
        try
        {
            await _vm.ReloadAsync();
        }
        catch (Exception ex)
        {
            _vm.StatusText = ex.Message;
        }
    }

    private async void OnNewClick(object? sender, RoutedEventArgs e)
        => await OpenEditAsync(null);

    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRow is { } row) await OpenEditAsync(row.Pack);
    }

    private async Task OpenEditAsync(BehaviorPack? existing)
    {
        var dialog = new BehaviorEditWindow { DataContext = new BehaviorEditViewModel(_vm.Main, existing) };
        await dialog.ShowDialog(this);
        if (dialog.Saved) _vm.IsDirty = true;
        await ReloadAsync();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var error = await _vm.DeleteSelectedAsync();
        if (error is not null) _vm.StatusText = error;
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        var error = await _vm.ApplyAsync();
        if (error is not null) _vm.StatusText = error;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
