using Avalonia.Controls;
using MyKeymap.Settings.Services;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Views;

/// <summary>
/// 主窗口：标题 "Setting"（AHK 侧以 "Setting ahk_exe MyKeymap.Settings.exe" 匹配窗口）。
/// 标题栏小图标透明化经 <see cref="TitleBarIconSuppressor"/> 统一接入 (与两个对话框窗口共用)。
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

        // 标题栏小图标透明化: 助手内部订阅 Opened(应用)/ScalingChanged(DPI 变化重放)/Closed(回收句柄)
        TitleBarIconSuppressor.Attach(this);

        Opened += (_, _) => _ = viewModel.InitializeAsync();
        Closing += (_, _) => viewModel.Session.Shutdown();
    }
}
