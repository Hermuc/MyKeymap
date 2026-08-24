using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MyKeymap.Settings.Services;

// ============================================================================
// BackendSession: settings.exe 后端连接抽象 (任务 #9 交付, 任务 #6 完善生命周期)
//
// 两种连接方式:
//   1. 子进程模式 (默认): 拉起 `settings.exe --headless`,
//      从其 stdout 首行 "MYKEYMAP_PORT=<端口>" 读取实际监听端口
//      (12333 被占用时 Go 侧会退到随机端口, 故必须按通告端口连接),
//      健康轮询 GET /config 直到真正可服务。
//   2. 直连模式 (--port <N>): 直接连接一个已运行的后端, 便于开发调试
//      与冒烟测试 (模拟部署目录由外部构造)。
//
// 生命周期 (任务 #6 已完善):
//   - Shutdown() 终止本会话拉起的子进程 (整树 Kill + 超时兜底, 幂等),
//     窗口 Closing / Dispatcher 退出 / 主入口 finally 三层兜底, 绝不留孤儿;
//   - 子进程意外退出: EnableRaisingEvents + Exited 事件 -> ChildProcessExited,
//     上层 (MainViewModel) 切错误态并提供「重试/重启后端」按钮, 不自动重启防风暴;
//   - 后端定位: --settings-exe/--backend-dir 优先, 缺省从 AppContext.BaseDirectory
//     推导 (bin\ 同目录或上级目录, 与部署根=bin 的上级的结构一致)。
// ============================================================================

/// <summary>会话启动参数 (从命令行解析或手工构造)。</summary>
public sealed class BackendSessionOptions
{
    /// <summary>非空时直连该端口 (不拉起子进程)。</summary>
    public int? DirectPort { get; init; }

    /// <summary>settings.exe 路径 (子进程模式); 缺省时从 AppContext.BaseDirectory 推导。</summary>
    public string? SettingsExePath { get; init; }

    /// <summary>settings.exe 工作目录; 缺省为 settings.exe 所在目录 (bin\, Go 依赖 ../data、./site、./templates 相对路径)。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>等待 MYKEYMAP_PORT= 通告行的超时。</summary>
    public TimeSpan PortAnnounceTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>健康轮询超时。</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 解析启动参数。支持:
    ///   --port &lt;N&gt;          直连已运行的后端
    ///   --settings-exe &lt;路径&gt; 显式指定 settings.exe
    ///   --backend-dir &lt;目录&gt;  显式指定 settings.exe 工作目录
    /// </summary>
    public static BackendSessionOptions Parse(string[] args)
    {
        int? port = null;
        string? exe = null;
        string? dir = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--port" when int.TryParse(args[i + 1], out var p):
                    port = p;
                    i++;
                    break;
                case "--settings-exe":
                    exe = args[i + 1];
                    i++;
                    break;
                case "--backend-dir":
                    dir = args[i + 1];
                    i++;
                    break;
            }
        }
        return new BackendSessionOptions { DirectPort = port, SettingsExePath = exe, WorkingDirectory = dir };
    }
}

/// <summary>settings.exe 后端会话: 连接建立、健康探测、API 暴露、子进程归属。</summary>
public sealed class BackendSession : IAsyncDisposable
{
    private readonly BackendSessionOptions _options;
    private Process? _child;
    private SettingsApiClient? _client;
    private Task? _stdoutDrain;
    private Task? _stderrDrain;
    private readonly List<string> _diagnostics = [];
    private volatile bool _suppressExitEvent;
    private IntPtr _jobHandle = IntPtr.Zero; // Job Object: GUI 进程死亡(含被强杀)时 OS 连带回收后端子进程树

    public BackendSession(BackendSessionOptions options) => _options = options;

    /// <summary>连接成功后可用的 API 客户端 (12 个契约端点)。</summary>
    public ISettingsApi? Api => _client;

    /// <summary>连接成功后可用的具体客户端 (含非契约的 GetRawTextAsync)。</summary>
    public SettingsApiClient? ConcreteApi => _client;

    /// <summary>后端实际监听端口。</summary>
    public int Port { get; private set; }

    /// <summary>true = 本会话拉起了子进程 (Shutdown 时需要终止它)。</summary>
    public bool OwnsChildProcess { get; private set; }

    /// <summary>启动失败时的人类可读原因 (展示在友好错误页)。</summary>
    public string? FailureReason { get; private set; }

    /// <summary>诊断行 (子进程 stdout/stderr 摘要), 错误页可展示。</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>
    /// 子进程意外退出事件 (参数为人类可读原因)。主动 Shutdown 不触发。
    /// 上层应切错误态并提供手动重启按钮, 不得自动无限重启 (防重启风暴)。
    /// </summary>
    public event Action<string>? ChildProcessExited;

    /// <summary>
    /// 建立连接。成功返回 true 并填充 <see cref="Api"/>; 失败返回 false 并填充 <see cref="FailureReason"/>。
    /// 永不抛出。
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        try
        {
            if (_options.DirectPort is { } port)
            {
                Port = port;
                _client = new SettingsApiClient(port);
                if (!await WaitForReadyAsync(_client, _options.ReadyTimeout, ct))
                {
                    FailureReason = $"无法连接到 http://localhost:{port} (--port 直连模式, 后端未运行或端口错误)";
                    DisposeClient();
                    return false;
                }
                return true;
            }

            return await StartChildProcessAsync(ct);
        }
        catch (Exception ex)
        {
            FailureReason = ex.Message;
            return false;
        }
    }

    /// <summary>原始文本 GET (Home 页 config_doc.html 用); 未连接或失败返回 null。</summary>
    public async Task<string?> GetRawTextAsync(string path, CancellationToken ct = default)
    {
        if (_client is null) return null;
        var resp = await _client.GetRawTextAsync(path, ct);
        return resp.Success ? resp.Value : null;
    }

    /// <summary>
    /// 关停会话: 终止本会话拉起的子进程 (整树)。幂等, 可在多层退出路径重复调用:
    /// MainWindow.Closing -> App.Exit 兜底 -> Program.Main finally 兜底。
    /// </summary>
    public void Shutdown()
    {
        _suppressExitEvent = true; // 主动关停不触发 ChildProcessExited
        DisposeClient();

        if (_child is not null)
        {
            try
            {
                if (!_child.HasExited)
                {
                    _child.Kill(entireProcessTree: true);
                    // 超时兜底: WaitForExit 限时, 绝不无限阻塞退出时序; 整树 Kill 后残留由 OS 回收
                    _child.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
                // 尽力而为: 进程可能已在退出竞态中消失 (Win32 ERROR_ACCESS_DENIED 等)
            }
            _child.Dispose();
            _child = null;
            OwnsChildProcess = false;
        }

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Shutdown();
        if (_stdoutDrain is not null) await Task.WhenAny(_stdoutDrain, Task.Delay(500));
        if (_stderrDrain is not null) await Task.WhenAny(_stderrDrain, Task.Delay(500));
    }

    // ------------------------------------------------------------------ 内部

    private async Task<bool> StartChildProcessAsync(CancellationToken ct)
    {
        // 解析 settings.exe 位置。
        // 部署形态 (与 Go 相对路径约定一致, 部署根=bin 的上级):
        //   settings.exe 位于 <部署根>\bin\, 工作目录 = bin\ (../data、./site、./templates);
        //   MyKeymap.Settings.exe 与 settings.exe 同在 bin\ (任务 #6 定案布局),
        //   兼容 GUI 在 bin\ 子目录 (如 bin\ui\) 的布局 (向上级查找)。
        // 开发形态: bin\Debug\net10.0\ 下没有 settings.exe, 用 --settings-exe/--port 指定。
        var exePath = ResolveSettingsExe();
        if (exePath is null)
        {
            FailureReason =
                $"未找到 settings.exe (查找位置: {SearchedCandidates()})。\n" +
                "可用 --port <N> 直连已运行的后端, 或用 --settings-exe <路径> 显式指定。";
            return false;
        }

        var workDir = _options.WorkingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(exePath))!;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--headless",
            WorkingDirectory = workDir, // 强依赖相对路径: ../data、./site、./templates
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            _child = Process.Start(psi);
        }
        catch (Exception ex)
        {
            FailureReason = $"启动 settings.exe 失败: {ex.Message}";
            return false;
        }
        if (_child is null)
        {
            FailureReason = "启动 settings.exe 失败 (Process.Start 返回 null)";
            return false;
        }
        OwnsChildProcess = true;

        // Job Object 兜底: 即使 GUI 被强杀 (AHK ProcessClose/任务管理器), 来不及走 Shutdown,
        // 进程退出时句柄关闭也会触发 KILL_ON_JOB_CLOSE, 连带终止 settings.exe 及其子进程树, 绝不留孤儿。
        AttachToJobObject(_child);

        // 意外退出监测: 子进程崩溃 (被任务管理器结束/自身异常) 时通知上层切错误态。
        // 主动 Shutdown 先置 _suppressExitEvent, 不会误报。
        _child.EnableRaisingEvents = true;
        _child.Exited += OnChildExited;

        var portTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stdoutDrain = Task.Run(() => DrainAsync(_child.StandardOutput, portTcs));
        _stderrDrain = Task.Run(async () =>
        {
            while (await _child.StandardError.ReadLineAsync(ct) is { } line)
            {
                AddDiagnostic("stderr: " + line);
            }
        });

        int port;
        try
        {
            port = await portTcs.Task.WaitAsync(_options.PortAnnounceTimeout, ct);
        }
        catch (TimeoutException)
        {
            FailureReason = "等待 settings.exe 通告端口超时 (未见 MYKEYMAP_PORT= 行)";
            Shutdown();
            return false;
        }
        catch (Exception ex)
        {
            FailureReason = $"读取 settings.exe 端口失败: {ex.Message}";
            Shutdown();
            return false;
        }

        Port = port;
        _client = new SettingsApiClient(port);
        if (!await WaitForReadyAsync(_client, _options.ReadyTimeout, ct))
        {
            FailureReason = $"settings.exe 已通告端口 {port}, 但健康探测 (GET /config) 超时";
            Shutdown();
            return false;
        }
        return true;
    }

    /// <summary>推导 settings.exe 路径; 找不到返回 null。</summary>
    private string? ResolveSettingsExe()
    {
        if (!string.IsNullOrEmpty(_options.SettingsExePath))
        {
            return File.Exists(_options.SettingsExePath) ? Path.GetFullPath(_options.SettingsExePath) : null;
        }
        return CandidatePaths().FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 候选位置: ① 与 GUI 同目录 (定案布局: MyKeymap.Settings.exe 与 settings.exe 同在 bin\);
    /// ② GUI 在 bin\ 子目录 (如 bin\ui\) 时向上级目录找, 保持「部署根=bin 的上级」结构。
    /// </summary>
    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "settings.exe"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "settings.exe"));
    }

    private static string SearchedCandidates() => string.Join("; ", CandidatePaths());

    /// <summary>把后端子进程挂入匿名 Job Object (KILL_ON_JOB_CLOSE): GUI 死亡时 OS 连带回收。</summary>
    private void AttachToJobObject(Process child)
    {
        try
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero) return;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };
            SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation,
                ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());

            if (!AssignProcessToJobObject(_jobHandle, child.Handle))
            {
                AddDiagnostic("job: AssignProcessToJobObject 失败 (err=" + Marshal.GetLastWin32Error() + ")");
            }
        }
        catch (Exception ex)
        {
            AddDiagnostic("job: 初始化失败: " + ex.Message);
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation, int cbJobObjectInformationLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>子进程退出处理: 区分主动关停与意外退出, 意外退出时向上层通告。</summary>
    private void OnChildExited(object? sender, EventArgs e)
    {
        if (_suppressExitEvent) return;

        int exitCode;
        try { exitCode = _child?.ExitCode ?? -1; }
        catch (Exception) { exitCode = -1; }

        List<string> tail;
        lock (_diagnostics) { tail = _diagnostics.TakeLast(5).ToList(); }
        var tailText = tail.Count > 0 ? "\n最后输出:\n" + string.Join("\n", tail) : "";

        // 诊断标记 (stdout 重定向时测试脚本可捕获; 显式刷新避免缓冲延迟)
        try
        {
            Console.WriteLine($"MYKEYMAP_BACKEND_EXITED code={exitCode}");
            Console.Out.Flush();
        }
        catch { /* 无控制台时忽略 */ }

        ChildProcessExited?.Invoke(
            $"settings.exe 后端进程意外退出 (退出码 {exitCode}), 配置服务已中断。{tailText}");
    }

    private async Task DrainAsync(StreamReader reader, TaskCompletionSource<int> portTcs)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            AddDiagnostic("stdout: " + line);
            // 端口通告行: headless 模式约定为 stdout 第一行 "MYKEYMAP_PORT=<端口>"
            if (line.StartsWith("MYKEYMAP_PORT=") && int.TryParse(line["MYKEYMAP_PORT=".Length..], out var p))
            {
                portTcs.TrySetResult(p);
            }
        }
        portTcs.TrySetException(new InvalidOperationException("settings.exe stdout 结束仍未收到 MYKEYMAP_PORT= 通告行"));
    }

    private static async Task<bool> WaitForReadyAsync(SettingsApiClient client, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var probe = await client.GetConfigAsync(ct);
            if (probe.Success) return true;
            await Task.Delay(100, ct);
        }
        return false;
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    private void AddDiagnostic(string line)
    {
        lock (_diagnostics)
        {
            _diagnostics.Add(line);
            if (_diagnostics.Count > 200) _diagnostics.RemoveRange(0, _diagnostics.Count - 200);
        }
    }
}
