using Avalonia.Controls;
using MyKeymap.Settings.Services;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 主窗口：标题必须为 "MyKeymap Setting"（AHK 侧以该标题匹配窗口）。
/// 生命周期: Opened -> InitializeAsync (连接后端/加载配置);
/// Closing -> 同步关停后端会话 (整树 Kill); 另有 App.Exit 与 Program.Main finally 两层兜底。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 模态提示对话框需要 Owner (保存 400 弹后端 message 等)
        if (viewModel.Messages is DialogMessageService dialogs)
        {
            dialogs.Owner = this;
        }

        Opened += (_, _) => _ = viewModel.InitializeAsync();
        Closing += (_, _) => viewModel.Session.Shutdown();
    }
}
