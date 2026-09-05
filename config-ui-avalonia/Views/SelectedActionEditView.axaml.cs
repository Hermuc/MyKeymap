using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MyKeymap.Settings.Services;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 选中动作编辑页视图的交互部分:
///   - 导出: 文件保存对话框写出方案 JSON (复刻 exportScheme);
///   - 导入: 粘贴对话框 -> <see cref="SelectedActionEditViewModel.TryImport"/>
///     (复刻 RuleList.vue 的导入弹窗, 文案 1019/1020/1021)。
/// </summary>
public partial class SelectedActionEditView : UserControl
{
    public SelectedActionEditView()
    {
        InitializeComponent();
        // 行为目录懒加载 (CONTRACTS §3.9): 首次进入编辑页时拉取, 供行为下拉覆盖推导
        Loaded += async (_, _) =>
        {
            if (BehaviorCatalog.Loaded) return;
            if (DataContext is SelectedActionEditViewModel vm && vm.Main.Session.Api is ISettingsApi api)
            {
                await BehaviorCatalog.LoadAsync(api);
                vm.Editor?.RefreshBehaviorOptions();
                vm.OnLanguageChanged();
            }
        };
    }

    /// <summary>打开行为库窗口; 关闭后刷新目录快照与行为下拉/规则展示。</summary>
    private async void OnManageBehaviorsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SelectedActionEditViewModel vm) return;
        if (vm.Main.Session.Api is not ISettingsApi api) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialog = new BehaviorLibraryWindow { DataContext = new BehaviorLibraryViewModel(vm.Main) };
        await dialog.ShowDialog(owner);
        await BehaviorCatalog.LoadAsync(api);
        vm.Editor?.RefreshBehaviorOptions();
        vm.OnLanguageChanged();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SelectedActionEditViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = I18n.T("984"),
            SuggestedFileName = vm.ExportFileName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        using var writer = new StreamWriter(stream);
        await writer.WriteAsync(vm.BuildExportJson());
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SelectedActionEditViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var imported = false;
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 180,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };

        var ok = new Button
        {
            Content = "OK",
            Background = new SolidColorBrush(Color.Parse("#4169E1")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = I18n.T("970"),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var dialog = new Window
        {
            Title = I18n.T("1019"),
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        ok.Click += (_, _) =>
        {
            var error = vm.TryImport(textBox.Text ?? "");
            if (error is not null)
            {
                vm.Main.ShowMessage(I18n.T("1019"), error);
                return; // 留在对话框内修正
            }
            imported = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = I18n.T("1020"), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#5F6368")), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = I18n.T("1021"), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#5F6368")) },
                textBox,
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
        _ = imported;
    }
}
