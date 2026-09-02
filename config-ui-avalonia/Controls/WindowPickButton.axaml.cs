using System;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyKeymap.Settings.Services;

namespace MyKeymap.Settings.Controls;

/// <summary>
/// 窗口拾取准星按钮 (方案 L5 控件层, 照 <see cref="HotkeyCapture"/> 范式):
///   - <see cref="Text"/> 双向绑定回填/追加目标 (如 WinTitle / WindowGroupRowVm.Value);
///   - <see cref="Kind"/> 决定标识符格式 (默认 TitleAndExe 组合: "{标题} ahk_exe {进程名}");
///   - <see cref="AppendMode"/> 为 true 时把结果作为新行追加 (多行字段, T8), 否则整值替换 (T7);
///   - <see cref="Picker"/> 未注入时回落 <see cref="WindowPickerService.Shared"/> 静态单例,
///     使控件在 DataTemplate 内零配置可用, 同时可注入 mock 供测试;
///   - 非提权识别失败 (FailedAccessDenied) 以 Popup 浮层提示 I18n 键 1081 (不参与布局测量), 约 4s 后自动收起。
/// 拾取交互机制 (钩子/高亮/取消) 全部封装在 <see cref="IWindowPickerService"/> 后, 本控件只负责装配与写回。
/// </summary>
public partial class WindowPickButton : UserControl
{
    /// <summary>回填/追加目标文本 (双向绑定; 写回经 SetCurrentValue 触发外部绑定源的 setter/校验)。</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<WindowPickButton, string>(nameof(Text), "", defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>匹配类型 (默认 TitleAndExe 组合)。</summary>
    public static readonly StyledProperty<WindowMatchKind> KindProperty =
        AvaloniaProperty.Register<WindowPickButton, WindowMatchKind>(nameof(Kind), WindowMatchKind.TitleAndExe);

    /// <summary>追加语义 (true=把结果作为新行追加, false=整值替换)。</summary>
    public static readonly StyledProperty<bool> AppendModeProperty =
        AvaloniaProperty.Register<WindowPickButton, bool>(nameof(AppendMode), false);

    /// <summary>拾取服务注入点 (null 时回落 <see cref="WindowPickerService.Shared"/>)。</summary>
    public static readonly StyledProperty<IWindowPickerService?> PickerProperty =
        AvaloniaProperty.Register<WindowPickButton, IWindowPickerService?>(nameof(Picker), null);

    // ---- 呈现属性 (DirectProperty: 标准 INPC 通知链路, 支撑编译绑定) ----
    /// <summary>拾取进行中 (防重入 + 按钮禁用视觉)。</summary>
    public static readonly DirectProperty<WindowPickButton, bool> IsPickingProperty =
        AvaloniaProperty.RegisterDirect<WindowPickButton, bool>(nameof(IsPicking), o => o.IsPicking);
    /// <summary>提示文本 (放 1081 非提权失败提示, 由 Popup 浮层呈现, 空则收起)。</summary>
    public static readonly DirectProperty<WindowPickButton, string> MessageTextProperty =
        AvaloniaProperty.RegisterDirect<WindowPickButton, string>(nameof(MessageText), o => o.MessageText);
    /// <summary>语言切换递增, 供控件内 ConverterParameter 文案重算。</summary>
    public static readonly DirectProperty<WindowPickButton, int> LanguageTickProperty =
        AvaloniaProperty.RegisterDirect<WindowPickButton, int>(nameof(LanguageTick), o => o.LanguageTick);

    private bool _isPicking;
    private string _messageText = "";
    private int _languageTick;
    private DispatcherTimer? _clearTimer;

    public WindowPickButton()
    {
        InitializeComponent();
        DataContext = this; // 控件自包含: 绑定源为控件自身属性

        I18n.Changed += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            I18n.Changed -= OnLanguageChanged;
            _clearTimer?.Stop();
            // 分离即清空提示: MessageText 为只读 DirectProperty (仅 getter), 走私有 setter (SetAndRaise) 而非 SetCurrentValue;
            // Popup.IsOpen 单向绑定 MessageText 非空 -> 清空即收起, 彻底消除分离后 Popup 滞留可能。
            MessageText = "";
        };
    }

    private void OnLanguageChanged() =>
        SetAndRaise(LanguageTickProperty, ref _languageTick, _languageTick + 1);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public WindowMatchKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool AppendMode
    {
        get => GetValue(AppendModeProperty);
        set => SetValue(AppendModeProperty, value);
    }

    public IWindowPickerService? Picker
    {
        get => GetValue(PickerProperty);
        set => SetValue(PickerProperty, value);
    }

    // ------------------------------------------------------------- 呈现属性

    public bool IsPicking { get => _isPicking; private set => SetAndRaise(IsPickingProperty, ref _isPicking, value); }
    public string MessageText { get => _messageText; private set => SetAndRaise(MessageTextProperty, ref _messageText, value); }
    public int LanguageTick => _languageTick;

    // ------------------------------------------------------------- 交互

    /// <summary>按钮 Click (抬起后触发): 进入拾取态并写回结果。</summary>
    private async void OnPickClick(object? sender, RoutedEventArgs e)
    {
        if (IsPicking) return; // 防重入
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        IsPicking = true;
        MessageText = "";
        _clearTimer?.Stop();
        try
        {
            var service = Picker ?? WindowPickerService.Shared;
            var result = await service.PickAsync(owner, new WindowPickerOptions { Kind = Kind }, CancellationToken.None)
                .ConfigureAwait(true);

            // PickAsync 的延续可能落在线程池线程 -> 回写 UI 前封送到 UI 线程
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyResult(result);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ApplyResult(result));
            }
        }
        catch (Exception ex)
        {
            // 无静态日志通道 (IMessageService 为需注入的模态对话框, 控件内不可达);
            // dev 侧写 Debug 输出, 用户侧复用瞬态 Popup 提示 I18n 1081 -> 故障可观测, 绝不留空 catch。
            Debug.WriteLine($"[WindowPickButton] PickAsync failed: {ex}");
            if (Dispatcher.UIThread.CheckAccess())
            {
                ShowTransientMessage(I18n.T("1081"));
            }
            else
            {
                Dispatcher.UIThread.Post(() => ShowTransientMessage(I18n.T("1081")));
            }
        }
        finally
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                IsPicking = false;
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsPicking = false);
            }
        }
    }

    /// <summary>结果分派 (必在 UI 线程): Success 写回 Text; FailedAccessDenied 内联提示; 其余不改 Text。</summary>
    private void ApplyResult(WindowPickResult result)
    {
        switch (result.Status)
        {
            case WindowPickStatus.Success:
                WriteBack(result.Text);
                break;
            case WindowPickStatus.FailedAccessDenied:
                ShowTransientMessage(I18n.T("1081"));
                break;
            // Cancelled / FailedNoWindow: 不改 Text
        }
    }

    /// <summary>写回 Text: AppendMode 追加为新行 ("\n" 分隔, 对齐 WindowGroupRowVm.Value), 否则整值替换。</summary>
    private void WriteBack(string text)
    {
        if (AppendMode)
        {
            // TrimEnd 后用 trimmed.Length==0 判空: old 全为换行 (如 "\n\n") 时不产生前导空行
            // (空行会被 Go/AHK NotBlankLines 过滤, 但仍避免写入无意义的空行)。
            var trimmed = (Text ?? "").TrimEnd('\r', '\n');
            var merged = trimmed.Length == 0 ? text : trimmed + "\n" + text;
            SetCurrentValue(TextProperty, merged);
        }
        else
        {
            SetCurrentValue(TextProperty, text);
        }
    }

    /// <summary>显示内联提示并起约 4s 一次性定时器自动清空。</summary>
    private void ShowTransientMessage(string message)
    {
        MessageText = message;
        _clearTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _clearTimer.Stop();
        _clearTimer.Tick -= OnClearTimerTick;
        _clearTimer.Tick += OnClearTimerTick;
        _clearTimer.Start();
    }

    private void OnClearTimerTick(object? sender, EventArgs e)
    {
        _clearTimer?.Stop();
        MessageText = "";
    }
}
