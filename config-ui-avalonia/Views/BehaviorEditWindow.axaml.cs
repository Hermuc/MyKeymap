using Avalonia.Controls;
using Avalonia.Interactivity;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 行为新建/编辑表单窗口 (模态): 保存经后端校验, 成功后置 <see cref="Saved"/> 由
/// 行为库窗口据此刷新目录并标记待生效。
/// </summary>
public partial class BehaviorEditWindow : Window
{
    private readonly BehaviorEditViewModel _vm;

    /// <summary>保存成功标记 (行为库窗口据此刷新 + 标记 IsDirty)。</summary>
    public bool Saved { get; private set; }

    public BehaviorEditWindow()
    {
        InitializeComponent();
        _vm = (BehaviorEditViewModel)DataContext!;
        Closed += (_, _) => _vm.UnsubscribeLanguage();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var error = await _vm.SaveAsync();
            if (error is not null)
            {
                _vm.StatusText = error;
                return;
            }
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            _vm.StatusText = ex.Message;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
