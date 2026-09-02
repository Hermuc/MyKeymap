using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using MyKeymap.Settings.Services.Win32;
using static MyKeymap.Settings.Services.Win32.NativeMethods;

namespace MyKeymap.Settings.Services;

// ============================================================================
// 拾取会话层 (L4): 「窗口拾取准星」交互机制。
//
// 采用方案 A (WH_MOUSE_LL + WH_KEYBOARD_LL 全局低级钩子, 跑在专用消息泵线程):
//   实时高亮当前指向窗口 + 吞掉提交/取消那次点击 + Esc/右键取消, 不遮屏。
//
// 两条铁律 (决定成败, 见方案 §四):
//   铁律1 —— 钩子回调体严格 O(1): 回调只写 volatile 坐标 / 置 dirty /
//            PostThreadMessage 后立即 return CallNextHookEx; 绝不在回调里调
//            WindowFromPoint/ProbeAt (会跨进程发 WM_NCHITTEST, 遇无响应程序阻塞≈1s,
//            超 LowLevelHooksTimeout 300ms 被静默摘钩 + 拖慢全局鼠标)。
//            探测搬到同线程 hwnd=NULL 定时器 (ThrottleMs 节流, 坐标变化才探)。
//   铁律2 —— 钩子绝不泄漏: try/finally + CancellationToken + owner.Closed 三重联动;
//            最外层 finally 强制 UnhookWindowsHookEx + KillTimer + 销毁高亮窗 + 恢复光标。
//            所有退出路径 (Success/Cancel/Exception/owner.Closed/ct) 统一恢复现场。
// ============================================================================

/// <summary>拾取结果状态。</summary>
public enum WindowPickStatus
{
    /// <summary>用户取消 (Esc / 右键 / owner 关闭 / ct)。</summary>
    Cancelled,

    /// <summary>成功拾取到窗口。</summary>
    Success,

    /// <summary>目标进程拒绝访问 (非提权运行探高完整性窗口), 需明确提示而非静默错值。</summary>
    FailedAccessDenied,

    /// <summary>提交点无窗口 / 落在自身进程 (会话内部据此保持存活让用户重瞄, 不外泄)。</summary>
    FailedNoWindow,
}

/// <summary>
/// 拾取结果。Cancelled / 失败态: <see cref="Text"/>="" 且 <see cref="Window"/>=null。
/// </summary>
public sealed record WindowPickResult(WindowPickStatus Status, WindowDescriptor? Window, WindowMatchKind Kind, string Text);

/// <summary>拾取行为选项 (AppendMode 是控件侧写回关注点, 不在本层)。</summary>
public sealed class WindowPickerOptions
{
    /// <summary>匹配类型 (默认组合: 窗口名 + 进程名)。</summary>
    public WindowMatchKind Kind { get; init; } = WindowMatchKind.TitleAndExe;

    /// <summary>是否显示实时高亮框 (默认开)。</summary>
    public bool Highlight { get; init; } = true;

    /// <summary>探测节流间隔 (毫秒, 默认 60ms)。</summary>
    public int ThrottleMs { get; init; } = 60;
}

/// <summary>窗口拾取服务抽象 (接口化 -> 机制可整体替换, 如降级为覆盖窗方案)。</summary>
public interface IWindowPickerService
{
    /// <summary>
    /// 进入拾取态: 鼠标变准星, 移动实时高亮窗口, 单击提交, Esc/右键取消。
    /// 钩子在本方法内部安装 (调用发生在控件 arm 之后), 避免把 arming 那次 up 当提交。
    /// </summary>
    System.Threading.Tasks.Task<WindowPickResult> PickAsync(
        Window owner, WindowPickerOptions options, CancellationToken ct = default);
}

/// <summary><see cref="IWindowPickerService"/> 默认实现 (全局低级钩子 + 专用消息泵线程)。</summary>
public sealed class WindowPickerService : IWindowPickerService
{
    /// <summary>进程级共享单例 (控件未注入自定义服务时回落此实例)。</summary>
    public static IWindowPickerService Shared { get; } = new WindowPickerService();

    /// <summary>防重入标志 (0=空闲, 1=拾取中): Shared 正在拾取时再次 PickAsync 直接返回 Cancelled。</summary>
    private int _busy;

    // M-2: 系统光标跨进程存活的启动自愈 + 崩溃兜底 (见 InstallStartupCursorGuard)。
    private static int s_cursorGuardInstalled;

    /// <summary>
    /// M-2 安全恢复入口: 无条件幂等重载系统光标方案 (SPI_SETCURSORS)。
    /// SetSystemCursor 改的是系统全局光标表, 不随进程退出回滚; 此入口只重载用户既有方案、不覆盖自定义,
    /// 可安全重复调用。供应用入口 (启动自愈 / AppDomain.UnhandledException / ProcessExit) 调用。
    /// </summary>
    public static void RestoreSystemCursors() => PickSession.RestoreSystemCursors();

    /// <summary>
    /// M-2 启动自愈: 应用入口启动时调用一次。
    /// ① 先无条件 <see cref="RestoreSystemCursors"/> —— 自愈上次会话硬崩溃 (FailFast/StackOverflow/
    ///    外部 TerminateProcess/断电重启后首次进入) 绕过 finally 残留的准星光标;
    /// ② 再注册 AppDomain.CurrentDomain.UnhandledException + ProcessExit → RestoreSystemCursors,
    ///    覆盖托管崩溃 / 正常退出路径 (finally 之外的硬崩溃仍靠下次启动的 ① 补偿)。
    /// 幂等: 多次调用只注册一次处理器。
    /// </summary>
    public static void InstallStartupCursorGuard()
    {
        // L-C: Main 启动关键路径, 光标自愈绝不该有能力让应用起不来 —— 启动恢复与两个 handler 均包 try/catch。
        try { RestoreSystemCursors(); } catch { }
        if (Interlocked.Exchange(ref s_cursorGuardInstalled, 1) == 1) return;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { try { RestoreSystemCursors(); } catch { } };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { RestoreSystemCursors(); } catch { } };
    }

    public async System.Threading.Tasks.Task<WindowPickResult> PickAsync(
        Window owner, WindowPickerOptions options, CancellationToken ct = default)
    {
        options ??= new WindowPickerOptions();
        if (owner is null)
        {
            return new WindowPickResult(WindowPickStatus.Cancelled, null, options.Kind, "");
        }

        // 防重入: 已有会话在跑则立即返回 Cancelled (不排队、不并发装钩子)
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return new WindowPickResult(WindowPickStatus.Cancelled, null, options.Kind, "");
        }

        try
        {
            return await PickSession.RunAsync(owner, options, ct).ConfigureAwait(true);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}

/// <summary>
/// 单次拾取会话: 承载钩子线程 / 消息泵 / 节流探测 / 高亮框 / 提交取消 / 三重联动清理。
/// 事件驱动状态机 (私有可变状态、无 UI 引用逻辑), 对齐 HotkeyCaptureCore 范式。
/// </summary>
internal sealed class PickSession
{
    private const string HighlightClassName = "MyKeymap.WindowPicker.Highlight";
    private const int FrameThickness = 3;      // 高亮框空心边宽 (px)
    private const uint HighlightColorRef = 0x00E16941; // #4169E1 -> COLORREF 0x00BBGGRR
    private static readonly IntPtr TimerId = new(1);

    private readonly Window _owner;
    private readonly WindowPickerOptions _options;
    private readonly IWindowProbe _probe;
    private readonly CancellationToken _ct;
    private readonly TaskCompletionSource<WindowPickResult> _tcs =
        new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

    // 保持委托存活 (防 GC 回收原生回调)
    private readonly LowLevelProc _mouseProc;
    private readonly LowLevelProc _kbProc;
    private readonly TimerProc _timerProc;

    private Thread? _thread;
    private CancellationTokenRegistration _ctReg;
    private EventHandler? _ownerClosedHandler;

    // 原生句柄 / 线程 id。High#3: _threadId 改 volatile —— RequestCancel 跨线程读, 需 acquire/release
    // 语义保证"读到非零 tid 时 pump 线程已 PeekMessage 预建消息队列", 从而 PostThreadMessage 必不丢。
    private volatile uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _kbHook;
    private IntPtr _highlightHwnd;

    // pump 线程私有交互状态 (钩子回调与定时器/循环同线程, 无数据竞争)
    private int _curX, _curY;
    private int _commitX, _commitY;
    private volatile bool _dirty;
    private volatile bool _cancelRequested;

    // Medium#4: 吞 down 后须吞配对 up, 避免向目标窗口投递不成对事件 (孤立 RBUTTONUP 可弹残留上下文菜单夺焦)
    private bool _swallowLButtonUp;
    private bool _swallowRButtonUp;
    private bool _swallowEscUp;

    // L-A: 排空中标志 —— DrainPairedUp 期间 OnTimer 直接 return, 不再探测/刷新高亮 (蓝框已在排空开头隐藏)。
    private bool _draining;
    // L-B: WM_APP_CANCEL 补发闩锁 —— L-4 定时器自愈只补发一次, 避免每 tick 重投。
    private bool _cancelPosted;

    // High#1: 探测短路缓存 —— root hwnd 未变则整条跨进程链 + region 重建全跳过
    private IntPtr _lastRootHwnd;
    private PixelRect _lastBounds;
    // High#1 精化: 上次完整探测时刻 (TickCount64) —— 纯 hwnd 短路外再加 500ms 时间兜底,
    // 让同一窗口拾取中自身移动/缩放 (最大化动画/Aero Snap/DPI 变化) 时高亮 ≤500ms 跟上变形 (完整探测 16.7/s→2/s)。
    private long _lastProbeTick;

    // L-3: 仅当本会话确实替换过系统光标 (ApplySystemCrossCursor 成功) 时 Cleanup 才恢复;
    // 避免两条早返回路径 (LoadCursor/CopyImage 失败) 无谓的全系统 SPI 广播 + 写 INI。
    private bool _cursorApplied;

    private WindowPickResult? _finalResult;

    // ---- 高亮窗口类 (进程内注册一次, 复用) ----
    private static bool s_classRegistered;
    private static IntPtr s_backgroundBrush;
    private static readonly WndProc s_wndProc = WndProcImpl;
    private static readonly object s_classLock = new();

    private PickSession(Window owner, WindowPickerOptions options, IWindowProbe probe, CancellationToken ct)
    {
        _owner = owner;
        _options = options;
        _probe = probe;
        _ct = ct;
        _mouseProc = MouseHookProc;
        _kbProc = KbHookProc;
        _timerProc = OnTimer;
    }

    /// <summary>启动会话: 拉起专用消息泵线程, 返回完成 Task (由 pump 线程 finally 兑现)。</summary>
    public static System.Threading.Tasks.Task<WindowPickResult> RunAsync(
        Window owner, WindowPickerOptions options, CancellationToken ct)
    {
        var session = new PickSession(owner, options, new Win32WindowProbe(), ct);
        return session.Start();
    }

    private System.Threading.Tasks.Task<WindowPickResult> Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "MyKeymap.WindowPicker" };

        // 三重联动取消 (2/3): CancellationToken + owner.Closed -> RequestCancel 唤醒 pump。
        // 设计决策: LostFocus / Deactivated 不作取消触发 —— 全局钩子不依赖焦点, 用户拾取时
        // 需点击其它窗口 (owner 必然失焦/失活), 若据此取消则功能不可用; Alt+Tab 等场景用户可 Esc 退出。
        _ctReg = _ct.Register(RequestCancel);
        _ownerClosedHandler = (_, _) => RequestCancel();
        _owner.Closed += _ownerClosedHandler;

        try
        {
            _thread.Start();
        }
        catch
        {
            // Low#14: 线程启动失败 -> 同步解绑三重联动 + 兜底 Cancelled (否则 _busy 永为 1, 按钮"点了没反应")
            if (_ownerClosedHandler is not null)
            {
                _owner.Closed -= _ownerClosedHandler;
                _ownerClosedHandler = null;
            }
            _ctReg.Dispose();
            _tcs.TrySetResult(Cancelled());
        }
        return _tcs.Task;
    }

    /// <summary>跨线程请求取消: 置标志 + PostThreadMessage 唤醒阻塞中的 GetMessage。</summary>
    private void RequestCancel()
    {
        // 先置标志再投递: 若 tid 尚未发布 (pump 未起), 标志会被 Run 内两处复查捕获; 否则 post 唤醒阻塞的 GetMessage
        _cancelRequested = true;
        Thread.MemoryBarrier(); // L-6: 形式化闭合 StoreLoad —— 保证置标志先于读 _threadId/投递对 pump 线程可见
        PostToPump(WM_APP_CANCEL);
    }

    /// <summary>
    /// 向 pump 线程投递消息 (非钩子路径专用: ct/owner.Closed 触发)。High#3: 检查 PostThreadMessage
    /// 返回值, 失败则有界重试。队列已由 pump 线程启动时 PeekMessage 预建, 正常首次即成功。
    /// </summary>
    private void PostToPump(uint msg)
    {
        var tid = _threadId;
        if (tid == 0) return;
        for (var i = 0; i < 5; i++)
        {
            if (PostThreadMessage(tid, msg, IntPtr.Zero, IntPtr.Zero)) return;
            Thread.Sleep(1); // 瞬时失败 (队列满) 兜底重试; 非钩子线程, 可承受
        }
    }

    // ------------------------------------------------------------------ pump 线程主体

    private void Run()
    {
        try
        {
            // High#3: 先 PeekMessage(PM_NOREMOVE) 强制创建本线程消息队列, 再发布 _threadId (volatile 写),
            // 从而保证任何线程读到非零 tid 时队列必已存在, PostThreadMessage 不会因队列未建而丢信号。
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _threadId = GetCurrentThreadId();
            Thread.MemoryBarrier(); // L-6: 形式化闭合 Dekker —— 保证"队列已建 + tid 发布"先于任何跨线程 RequestCancel 读取

            EnsureHighlightClass();

            if (_cancelRequested)
            {
                _finalResult = Cancelled();
                return;
            }

            // 安装全局低级钩子 (在 PickAsync 内部/控件 arm 之后, 避免把 arming 那次 up 当提交)
            var hMod = GetModuleHandle(null);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                // Low#13: SetWindowsHookEx 已 SetLastError=true, 记录错误码
                System.Diagnostics.Debug.WriteLine($"[WindowPicker] WH_MOUSE_LL 装钩失败 err={Marshal.GetLastWin32Error()}");
            }
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            if (_kbHook == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowPicker] WH_KEYBOARD_LL 装钩失败 err={Marshal.GetLastWin32Error()}"); // Low#13
            }
            if (_mouseHook == IntPtr.Zero || _kbHook == IntPtr.Zero)
            {
                // 钩子安装失败: 无法进行拾取, 按取消处理 (finally 会摘掉可能已装上的那个)
                _finalResult = Cancelled();
                return;
            }

            if (_options.Highlight)
            {
                CreateHighlightWindow();
            }

            // High#2: 系统级准星光标 (SetCursor 在钩子线程只改本线程、系统不显示, 已废弃)
            _cursorApplied = ApplySystemCrossCursor(); // L-3: 记录是否真的替换过, Cleanup 据此决定是否恢复

            var throttle = (uint)Math.Max(16, _options.ThrottleMs);
            if (SetTimer(IntPtr.Zero, TimerId, throttle, _timerProc) == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[WindowPicker] SetTimer 失败, 高亮不刷新 (钩子仍可提交/取消)"); // Low#13
            }

            // High#3: 进入 GetMessage 阻塞前再复查一次取消 —— 覆盖"取消发生在发布 tid 之后、入泵之前"的竞态,
            // 否则 GetMessage 永久阻塞 -> 双钩滞留系统 + _tcs 永不兜现 + _busy 永为 1。
            if (_cancelRequested)
            {
                _finalResult = Cancelled();
                return;
            }

            PumpMessages();
        }
        catch
        {
            // 任何异常都不外泄到调用方 UI 线程; 统一降级为取消 (finally 保证恢复现场)
            _finalResult ??= Cancelled();
        }
        finally
        {
            // H-1 + M-B: 摘钩 (Cleanup→UnhookWindowsHookEx) 之前有界排空配对 UP, 覆盖 break/异常/早返回全部退出路径
            // (旧实现写在 PumpMessages 末尾, HandleCommit 抛异常穿出时会跳过排空)。排空自身必须 try/catch ——
            // 否则其异常会跳过 Cleanup 致双钩泄漏 (铁律2)。早返回路径下三个 _swallow* 必为 false → 排空首行即 return。
            try { DrainPairedUp(); } catch { }
            // 铁律2 + Medium#5: Cleanup 自身异常绝不可外逃 (后台线程未处理异常 = 进程崩溃 + _tcs 永不兑现)。
            // try/catch 包住 Cleanup, 内层 finally 无条件兑现 _tcs。
            try
            {
                Cleanup();
            }
            catch
            {
                // 记录, 绝不外逃
            }
            finally
            {
                _tcs.TrySetResult(_finalResult ?? Cancelled());
            }
        }
    }

    private void PumpMessages()
    {
        // hwnd=NULL 消息泵: 定时器回调经 DispatchMessage 驱动 (节流探测在此线程执行)
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_CANCEL)
            {
                _finalResult = Cancelled();
                break;
            }
            if (msg.message == WM_APP_COMMIT)
            {
                HandleCommit();
                if (_finalResult is not null)
                {
                    break;
                }
                // FailedNoWindow (自身/无窗口): 保持会话存活让用户重瞄, 不退出
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        // M-B: 排空已上移到 Run 的 finally (覆盖 break/异常/早返回全部退出路径), 此处不再调用。
    }

    /// <summary>
    /// H-1: 终止路径吞配对 UP 的有界排空 (由 Run 的 finally 在 Cleanup/摘钩之前调用, 覆盖全部退出路径)。
    /// 背景: 吞掉 DOWN 后, pump 在同一次消息循环迭代内同步走完 break→Run.finally→Cleanup→UnhookWindowsHookEx;
    /// 而 HandleCommit 完整探测仅 ~0.03ms, 远早于人手物理抬起 (典型 50-150ms) —— 摘钩必然早于配对 UP 到达,
    /// 导致孤立 WM_RBUTTONUP 投递给光标下目标窗口 → DefWindowProc 生成 WM_CONTEXTMENU → 弹右键菜单/夺焦
    /// (文档化主取消路径右键 100% 复现)。左键提交产生孤立 WM_LBUTTONUP (DOWN 已吞不会误激活, 危害低);
    /// Esc 产生孤立 WM_KEYUP (基本无害)。注: FailedNoWindow 重瞄路径不退出会话、钩子仍在, 不经此排空。
    /// H-A 有界硬约束 (铁律2, 与定时器完全解耦): 用 PeekMessage(PM_REMOVE) + Thread.Sleep(1) 轮询替代 GetMessage 阻塞 ——
    /// 有界性不再依赖 SetTimer 成功/ThrottleMs 唤醒 (旧实现若 SetTimer 失败且配对 UP 被别的 LL 钩子吞掉/系统已摘钩/
    /// 设备断开而永不到达, GetMessage 会无限阻塞 → 双钩永久滞留 + 全局左键失效)。
    /// Low-1: 现上界 ≈ budgetMs + 一个 Sleep 粒度 (Windows 默认定时器精度下 Thread.Sleep(1) 实测约 15.6~20ms,
    /// 故实际最坏 ≈ budgetMs + 20ms); 有界性无条件成立, 与 SetTimer 成败 / ThrottleMs 大小完全无关。
    /// PeekMessage 同样派发 sent message → LL 钩子回调照常触发 → UP 被吞并清标志。
    /// Low-2: DispatchMessage 行为按消息类型分 —— WM_APP_COMMIT / WM_APP_CANCEL 等纯线程消息 (hwnd=NULL 且非 WM_TIMER)
    /// 经 DispatchMessage 为 no-op, 故不重入 HandleCommit; 而 WM_TIMER 同为 hwnd=NULL 却【会】被 DispatchMessage 派给 OnTimer
    /// (正是 L-A 的 _draining 守卫所依赖的机制), 排空期由 OnTimer 首段 if(_draining) return; 立即返回 → 零探测 / 零高亮刷新 /
    /// 零跨进程阻塞。结论「不重入 HandleCommit」不变 (OnTimer 从不调 HandleCommit), 但补齐依据链条, 免读者误以为排空期
    /// OnTimer 根本不触发、从而误判 _draining 守卫可有可无。
    /// 排空期间若又收 DOWN, 钩子照常吞 + 置标志, 循环仍受 budgetMs 约束 → 双钩必在 ≤budgetMs 内被摘除。
    /// Low-3 固有取舍登记: 排空发生在摘钩之前 (H-1 目的所在), 故期间钩子仍在链上 —— 提交/取消后 ≤budgetMs 内用户的额外点击
    /// 会被全局静默吞掉 (含 DBLCLK), 无视觉反馈; 这是 H-1「吞配对 UP」设计的固有代价、有界可接受, 非新 bug, 勿当新缺陷重查。
    /// </summary>
    private void DrainPairedUp(int budgetMs = 500)   // M-A: 预算 300→500
    {
        if (!_swallowLButtonUp && !_swallowRButtonUp && !_swallowEscUp) return;
        // L-A: 排空期间不再探测/刷新高亮 —— 先隐藏蓝框并置 _draining (让 OnTimer 直接 return),
        // 避免 Esc/右键取消后蓝框再亮/挪一下; 顺带把 M-1 跨进程阻塞暴露面在排空期收窄到零。
        HideHighlight();
        _draining = true;
        var deadline = Environment.TickCount64 + budgetMs;
        while ((_swallowLButtonUp || _swallowRButtonUp || _swallowEscUp) && Environment.TickCount64 < deadline)
        {
            if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                if (msg.message == WM_QUIT) break;   // 不吞退出信号
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            else
            {
                Thread.Sleep(1);                     // 有界性无条件成立 (不依赖定时器), 且让出 CPU
            }
        }
    }

    // ------------------------------------------------------------------ 提交 / 探测

    private void HandleCommit()
    {
        var res = _probe.ProbeAt(new PixelPoint(_commitX, _commitY));
        switch (res.Status)
        {
            case WindowPickStatus.Success when res.Window is not null:
                var text = WindowMatchFormatter.Format(res.Window, _options.Kind);
                _finalResult = new WindowPickResult(WindowPickStatus.Success, res.Window, _options.Kind, text);
                break;
            case WindowPickStatus.FailedAccessDenied:
                _finalResult = new WindowPickResult(WindowPickStatus.FailedAccessDenied, null, _options.Kind, "");
                break;
            default:
                // FailedNoWindow: 不取消, 等待用户重瞄
                break;
        }
    }

    // ------------------------------------------------------------------ M-1 已知残留 (本轮不修, 仅登记)
    // M-1: 探测 (WindowFromPoint / GetWindowText / OpenProcess ...) 仍跑在 pump 线程 (WM_TIMER 回调内)。
    //   当悬停/提交点命中一个已挂死 (无响应) 的第三方窗口时, 跨进程 WM_NCHITTEST / WM_GETTEXT 会阻塞,
    //   可能超过 LowLevelHooksTimeout(300ms) 被系统静默摘钩 → 拾取器静默失灵 + 数百 ms 迟滞, 不损坏系统状态。
    //   两条独立恢复路径: ① 关掉那个挂死的第三方窗口, 探测解除阻塞后拾取器自行恢复;
    //   ② 关闭我们的设置窗口 (owner.Closed → RequestCancel) 主动结束会话。
    //   方案 §四铁律1「回调 O(1) + 探测搬 WM_TIMER」已实现, M-1 是超出该方案的更深议题, 属已知残留, 后续迭代处理。
    //   未来增强方向 (二选一或并用): ① 把探测搬到独立 worker 线程, pump 线程只收结果 (彻底解耦阻塞);
    //   ② 缓解: GetWindowText 换成 SendMessageTimeout(SMTO_ABORTIFHUNG, 50ms), 对挂死窗口快速返回不阻塞。
    //   注意: 勿误以为已根除 —— 本轮【未做】此改造。

    /// <summary>
    /// 节流探测 (WM_TIMER): 绝不放在钩子回调里。
    /// High#1: 坐标变化时先只做 WindowFromPoint + GetAncestor(GA_ROOT); root hwnd 未变且未超时间兜底则立即 return ——
    /// 不跑整条跨进程链 (GetWindowText/OpenProcess/QueryFullProcessImageNameW/DwmGetWindowAttribute...)、
    /// 不重建 region、不 SetWindowPos, 杜绝鼠标每移动一格就重探导致的输入迟滞。
    /// </summary>
    private void OnTimer(IntPtr hWnd, uint uMsg, IntPtr nIdEvent, uint dwTime)
    {
        // L-4 + L-B: 自愈 —— 若已请求取消但 pump 迟迟没收到 WM_APP_CANCEL (PostToPump 5 次重试全失败的极端情况),
        // 定时器每 ThrottleMs 必触发, 在此重投, 防双钩滞留系统 (铁律2 兜底); _cancelPosted 闩锁保证只补发一次, 不每 tick 重投。
        if (_cancelRequested)
        {
            if (!_cancelPosted)
            {
                _cancelPosted = true;
                PostThreadMessage(_threadId, WM_APP_CANCEL, IntPtr.Zero, IntPtr.Zero);
            }
            return;
        }

        // L-A: 排空期间 (DrainPairedUp 已置位) 不再探测/刷新高亮, 直接 return —— 蓝框已在排空开头隐藏。
        if (_draining) return;

        if (!_dirty) return;
        _dirty = false;

        // 先只做命中测试取 root hwnd (廉价、同线程)
        var hit = WindowFromPoint(new POINT(_curX, _curY));
        var root = hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, GA_ROOT);

        // High#1 精化: root hwnd 未变 且 距上次完整探测 <500ms 才短路。纯 hwnd 短路会让同一窗口在拾取中
        // 自身移动/缩放 (最大化动画 / Aero Snap / DPI 变化) 时高亮停在旧边界, 直到光标跨入另一 root;
        // 加时间兜底后完整探测频率 16.7/s→2/s (代价 0.06ms/s), 高亮 ≤500ms 跟上窗口自身变形。
        if (root == _lastRootHwnd && Environment.TickCount64 - _lastProbeTick < 500) return;

        // root 变化 或 超时间兜底 -> 跑完整探测 + 重定位高亮
        var res = _probe.ProbeAt(new PixelPoint(_curX, _curY));
        // L-5: 缓存键以实际探测到的窗口为准 (ProbeAt 内部做 GA_ROOT 归一 + UWP 子窗口回溯, 结果 hwnd 才是权威);
        // 探测失败/无窗口时 res.Window 为 null, 回落 root, 保证下次同点位仍短路不空转。
        _lastRootHwnd = res.Window?.Hwnd ?? root;
        _lastProbeTick = Environment.TickCount64;

        if (res.Status == WindowPickStatus.Success && res.Window is not null)
        {
            UpdateHighlight(res.Window.Bounds);
        }
        else
        {
            HideHighlight();
        }
    }

    // ------------------------------------------------------------------ 钩子回调 (严格 O(1))

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = wParam.ToInt32();
            switch (m)
            {
                case (int)WM_MOUSEMOVE:
                {
                    // 铁律1: 回调严格 O(1) —— 只写坐标 + 置 dirty, 探测交给 WM_TIMER
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _curX = data.pt.X;
                    _curY = data.pt.Y;
                    _dirty = true;
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }
                case (int)WM_LBUTTONDOWN:
                case (int)WM_LBUTTONDBLCLK:
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _curX = data.pt.X;
                    _curY = data.pt.Y;
                    _commitX = _curX;
                    _commitY = _curY;
                    _swallowLButtonUp = true; // Medium#4: 标记吞配对 UP, 防向目标投递不成对事件
                    PostThreadMessage(_threadId, WM_APP_COMMIT, IntPtr.Zero, IntPtr.Zero);
                    return new IntPtr(1);      // 吞掉这次 down (防误激活目标程序)
                }
                case (int)WM_LBUTTONUP:
                {
                    if (_swallowLButtonUp) { _swallowLButtonUp = false; return new IntPtr(1); } // Medium#4
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }
                case (int)WM_RBUTTONDOWN:
                case (int)WM_RBUTTONDBLCLK:
                {
                    _swallowRButtonUp = true; // Medium#4: 防孤立 RBUTTONUP 弹残留上下文菜单夺焦
                    PostThreadMessage(_threadId, WM_APP_CANCEL, IntPtr.Zero, IntPtr.Zero);
                    return new IntPtr(1);
                }
                case (int)WM_RBUTTONUP:
                {
                    if (_swallowRButtonUp) { _swallowRButtonUp = false; return new IntPtr(1); } // Medium#4
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KbHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.vkCode == VK_ESCAPE)
            {
                if (m == (int)WM_KEYDOWN || m == (int)WM_SYSKEYDOWN)
                {
                    _swallowEscUp = true; // Medium#4: 标记吞配对 UP
                    PostThreadMessage(_threadId, WM_APP_CANCEL, IntPtr.Zero, IntPtr.Zero);
                    return new IntPtr(1);  // 吞掉 Esc down
                }
                if ((m == (int)WM_KEYUP || m == (int)WM_SYSKEYUP) && _swallowEscUp)
                {
                    _swallowEscUp = false;
                    return new IntPtr(1);  // Medium#4: 吞掉配对 Esc up
                }
            }
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ------------------------------------------------------------------ 高亮框

    private static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProc(hWnd, msg, wParam, lParam);

    /// <summary>进程内注册高亮窗口类一次 (背景刷填主题色 #4169E1, 区域裁剪成空心框)。</summary>
    private static void EnsureHighlightClass()
    {
        if (s_classRegistered) return;
        lock (s_classLock)
        {
            if (s_classRegistered) return;
            if (s_backgroundBrush == IntPtr.Zero)
            {
                s_backgroundBrush = CreateSolidBrush(HighlightColorRef);
            }

            var wc = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = GetModuleHandle(null),
                hbrBackground = s_backgroundBrush,
                lpszClassName = HighlightClassName,
            };
            var atom = RegisterClass(ref wc);
            if (atom == 0)
            {
                // Low#15: 注册失败 -> 释放刚建的背景刷, 不留悬挂 GDI 对象; 仍置位避免每次重试,
                // CreateWindowExW 若类不存在将返回 0 -> 优雅降级为无高亮
                if (s_backgroundBrush != IntPtr.Zero)
                {
                    DeleteObject(s_backgroundBrush);
                    s_backgroundBrush = IntPtr.Zero;
                }
            }
            s_classRegistered = true;
        }
    }

    private void CreateHighlightWindow()
    {
        _highlightHwnd = CreateWindowExW(
            WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            HighlightClassName, null, WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_highlightHwnd != IntPtr.Zero)
        {
            SetLayeredWindowAttributes(_highlightHwnd, 0, 255, LWA_ALPHA);
        }
    }

    /// <summary>
    /// 移动/调整高亮框到目标窗口边界 (物理像素)。
    /// High#1: 位置未变加 SWP_NOMOVE、尺寸未变加 SWP_NOSIZE; region 重建仅 (w,h) 变化时执行, 杜绝每 tick 整窗重绘。
    /// Low#15: 尺寸变化时先 ApplyFrameRegion 再 SetWindowPos(SHOWWINDOW), 避免露出旧区域的一瞬。
    /// </summary>
    private void UpdateHighlight(PixelRect b)
    {
        if (_highlightHwnd == IntPtr.Zero) return;
        var x = b.X;
        var y = b.Y;
        var w = Math.Max(1, b.Width);
        var h = Math.Max(1, b.Height);

        var posChanged = x != _lastBounds.X || y != _lastBounds.Y;
        var sizeChanged = w != _lastBounds.Width || h != _lastBounds.Height;

        if (sizeChanged)
        {
            ApplyFrameRegion(w, h); // 先重建区域 (Low#15)
        }

        var flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
        if (!posChanged) flags |= SWP_NOMOVE;
        if (!sizeChanged) flags |= SWP_NOSIZE;
        SetWindowPos(_highlightHwnd, HWND_TOPMOST, x, y, w, h, flags);

        _lastBounds = new PixelRect(x, y, w, h);
    }

    /// <summary>外框减内框 = FrameThickness px 空心区域; SetWindowRgn 后系统接管 outer 生命周期。</summary>
    private void ApplyFrameRegion(int w, int h)
    {
        var outer = CreateRectRgn(0, 0, w, h);
        var inner = CreateRectRgn(FrameThickness, FrameThickness,
            Math.Max(FrameThickness, w - FrameThickness), Math.Max(FrameThickness, h - FrameThickness));
        CombineRgn(outer, outer, inner, RGN_DIFF);
        DeleteObject(inner);
        SetWindowRgn(_highlightHwnd, outer, true);
    }

    private void HideHighlight()
    {
        if (_highlightHwnd != IntPtr.Zero)
        {
            ShowWindow(_highlightHwnd, SW_HIDE);
        }
    }

    // ------------------------------------------------------------------ 清理 (铁律2)

    private void Cleanup()
    {
        // Medium#5 + 铁律2: 每步各自 try/catch 兜底, UnhookWindowsHookEx 提到最前 (最优先摘钩, 防全局输入迟滞)。
        try { if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; } } catch { }
        try { if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; } } catch { }
        try { KillTimer(IntPtr.Zero, TimerId); } catch { }
        try { if (_highlightHwnd != IntPtr.Zero) { DestroyWindow(_highlightHwnd); _highlightHwnd = IntPtr.Zero; } } catch { } // 于创建它的 pump 线程销毁

        // High#2 + L-3: 仅当本会话确实替换过系统光标才恢复 (SPI_SETCURSORS 重载用户方案, 保留自定义);
        // 避免两条早返回路径 (LoadCursor/CopyImage 失败, _cursorApplied=false) 无谓的全系统 SPI 广播 + 写 INI。
        // M-2: 极端硬崩溃 (FailFast/StackOverflow/外部 Terminate/断电) 绕过 finally 时仍可能滞留准星 ——
        // 由应用入口的启动无条件自愈 (WindowPickerService.InstallStartupCursorGuard) 于下次启动补偿。
        try { if (_cursorApplied) RestoreSystemCursors(); } catch { }

        // 解除三重联动 (2/3): ct + owner.Closed
        try
        {
            if (_ownerClosedHandler is not null)
            {
                _owner.Closed -= _ownerClosedHandler;
                _ownerClosedHandler = null;
            }
        }
        catch { }
        try { _ctReg.Dispose(); } catch { }
    }

    /// <summary>
    /// High#2: 会话开始 —— 系统级把标准箭头 (OCR_NORMAL) 替换为准星。返回是否真的替换成功 (供 L-3 守卫)。
    /// 关键陷阱: SetSystemCursor 会销毁传入句柄, 必须传 CopyImage 私有副本, 绝不可传 LoadCursor 的共享系统句柄
    /// (否则销毁系统共享句柄 -> 全局光标损坏)。
    /// </summary>
    private static bool ApplySystemCrossCursor()
    {
        var cross = LoadCrossCursor();
        if (cross == IntPtr.Zero) return false;
        var copy = CopyImage(cross, IMAGE_CURSOR, 0, 0, LR_COPYRETURNORG);
        // L-1: 加固不变量 —— CopyImage(LR_COPYRETURNORG) 实测返回真私有副本; 但若返回零或与源同一句柄,
        // 绝不把 LoadCursor 的共享系统句柄交给会销毁它的 SetSystemCursor (否则全局光标损坏)。
        if (copy == IntPtr.Zero || copy == cross) return false;
        // L-2: SetSystemCursor 成功则 copy 交系统接管/销毁; 失败时手动 DestroyCursor 防 GDI 句柄泄漏。
        if (!SetSystemCursor(copy, OCR_NORMAL))
        {
            DestroyCursor(copy);
            return false;
        }
        return true;
    }

    /// <summary>
    /// High#2: 会话结束 —— SPI_SETCURSORS 重载用户光标方案恢复 (优于强制 IDC_ARROW, 保留自定义方案)。
    /// M-2: 改 internal static 供 <see cref="WindowPickerService"/> 暴露的安全恢复入口调用 (启动自愈 + 崩溃兜底)。
    /// 幂等: SPI_SETCURSORS 只重载用户既有方案、不覆盖自定义, 可无条件重复调用。
    /// </summary>
    internal static void RestoreSystemCursors()
    {
        // L-E: SetSystemCursor 只改内存中的系统光标表、不写注册表; 恢复只需从注册表重载用户方案 + 广播变更,
        // SPIF_SENDCHANGE 已足够。去掉 SPIF_UPDATEINIFILE (它会把原值再写一遍用户 profile, 多余)。
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
    }

    private WindowPickResult Cancelled()
        => new(WindowPickStatus.Cancelled, null, _options.Kind, "");
}
