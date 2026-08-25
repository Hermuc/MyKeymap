using System.Globalization;
using Avalonia.Data.Converters;

namespace MyKeymap.Settings.Services;

// ============================================================================
// 双语资源方案 (复刻 config-ui/src/store/language-map.ts 的 label:NNN 机制)
//
// Vue 侧: translate('label:501') -> languageMap[501][options.language] ?? en
// Avalonia 侧: I18n.T("501") -> Map[key][Language] ?? Map[key]["en"] ?? key
//
// 语言切换实时生效:
//   - 单例标签: AXAML 里 Text="{Binding LanguageTick, Converter={StaticResource Tr},
//     ConverterParameter=501}", 语言变化时 ViewModel 递增 LanguageTick 触发重新求值;
//   - 列表项标签: ViewModel 预先翻译进条目模型, 语言变化时重建标签文本。
// 额外键 (900+) 为 Avalonia 移植新增文案 (hideMatrix / pathVariables / 帮助页等),
// Vue 侧对应控件被注释隐藏, 本移植按任务规格补齐。
// ============================================================================

/// <summary>静态双语服务: 全局当前语言 + 文案表 + 翻译查询。</summary>
public static class I18n
{
    public const string Zh = "zh";
    public const string En = "en";

    private static string _language = Zh;

    /// <summary>当前语言 ("zh"/"en")。赋值触发 <see cref="Changed"/>。</summary>
    public static string Language
    {
        get => _language;
        set
        {
            var normalized = string.Equals(value, Zh, StringComparison.OrdinalIgnoreCase) ? Zh : En;
            if (_language == normalized) return;
            _language = normalized;
            Changed?.Invoke();
        }
    }

    /// <summary>语言变化事件 (ViewModel 订阅后递增 LanguageTick 以刷新绑定)。</summary>
    public static event Action? Changed;

    /// <summary>
    /// 翻译。key 可带 "label:" 前缀 (与 Vue comment 兼容)。
    /// 查不到当前语言时回退英文, 再查不到原样返回。
    /// </summary>
    public static string T(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var k = key.StartsWith("label:", StringComparison.Ordinal) ? key["label:".Length..] : key;
        if (Map.TryGetValue(k, out var pair))
        {
            var text = _language == Zh ? pair.Zh : pair.En;
            if (!string.IsNullOrEmpty(text)) return text;
            // 回退: 目标语言缺失时用另一语言
            text = _language == Zh ? pair.En : pair.Zh;
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return k;
    }

    /// <summary>把 <see cref="Language"/> 设为 config 里的 language 字段值 (不触发 UI 时可直接调)。</summary>
    public static void ApplyConfigLanguage(string? configLanguage)
    {
        Language = string.Equals(configLanguage, En, StringComparison.OrdinalIgnoreCase) ? En : Zh;
    }

    private static readonly Dictionary<string, (string? Zh, string? En)> Map = new()
    {
        // ---- window ----
        ["1"] = ("关闭窗口", "Close"),
        ["2"] = ("切换到上一个窗口", "Previous window"),
        ["3"] = ("在当前程序的窗口间轮换", "Cycle through app windows"),
        ["4"] = ("窗口管理 (EDSF切换X关闭,空格选择)", "Task switcher (EDSF=↑↓←→,X=Delete"),
        ["5"] = ("上一个虚拟桌面", "Previous virtual desktop"),
        ["6"] = ("下一个虚拟桌面", "Next virtual desktop"),
        ["7"] = ("移动窗口到下一个显示器", "Move window to next monitor"),
        ["8"] = ("关闭窗口 (杀进程)", "Kill the active process"),
        ["9"] = ("关闭同类窗口", "Close all app windows"),
        ["10"] = ("窗口最小化", "Minimize"),
        ["11"] = ("窗口最大化或还原", "Maximize"),
        ["12"] = ("窗口居中 (1200x800)", "Center (1200x800)"),
        ["13"] = ("窗口居中 (1370x930)", "Center (1370x930)"),
        ["14"] = ("切换窗口置顶状态", "Always on top"),
        ["15"] = ("让窗口随鼠标拖动", "Drag window"),
        ["16"] = ("绑定当前窗口 (长按绑定,短按激活)", "Bind window by long press"),

        // ---- system ----
        ["17"] = ("锁屏", "Lock the screen"),
        ["18"] = ("睡眠", "Sleep"),
        ["19"] = ("关机", "Shutdown"),
        ["20"] = ("重启", "Reboot"),
        ["21"] = ("音量调节", "Volume control"),
        ["22"] = ("显示器亮度调节", "Monitor brightness control"),
        ["23"] = ("重启文件资源管理器", "Restart explorer.exe"),
        ["24"] = ("复制文件路径或纯文本", "Copy full path of a file"),
        ["2401"] = ("静音当前应用", "Mute active app"),
        ["2402"] = ("打开当前程序所在目录", "Show active process in folder"),

        // ---- 总览页 (Home) 编辑 ----
        ["2404"] = ("恢复默认", "Restore Default"),
        ["2405"] = ("支持 Markdown 语法: # 标题、- 列表、[文字](链接)、![图片](地址)、`代码`; 保存后立即生效, 清空内容并保存可恢复默认文档", "Markdown supported: # heading, - list, [text](link), ![image](url), `code`; takes effect after save; clear and save to restore the default document"),
        ["2406"] = ("编辑总览页", "Edit Overview Page"),
        ["2407"] = ("点击此区域编辑总览", "Click here to edit the overview"),

        // ---- mouse ----
        ["25"] = ("鼠标上移", "Mouse up"),
        ["26"] = ("鼠标下移", "Mouse down"),
        ["27"] = ("鼠标左移", "Mouse left"),
        ["28"] = ("鼠标右移", "Mouse right"),
        ["29"] = ("滚轮上滑", "Wheel up"),
        ["30"] = ("滚轮下滑", "Wheel down"),
        ["31"] = ("滚轮左滑", "Wheel left"),
        ["32"] = ("滚轮右滑", "Wheel right"),
        ["33"] = ("鼠标左键", "Left button"),
        ["34"] = ("鼠标右键", "Right button"),
        ["35"] = ("鼠标中键", "Middle button"),
        ["36"] = ("鼠标左键按下 (之后按空格松开)", "Left button down ( Space=Release )"),
        ["37"] = ("移动鼠标到活动窗口", "Move mouse to active window"),

        // ---- text ----
        ["38"] = ("光标 - 上移", "Up"),
        ["39"] = ("光标 - 下移", "Down"),
        ["40"] = ("光标 - 左移", "Left"),
        ["41"] = ("光标 - 右移", "Right"),
        ["42"] = ("光标 - 跳到行首", "Home"),
        ["43"] = ("光标 - 跳到行尾", "End"),
        ["44"] = ("光标 - 上一单词", "Ctrl + Left"),
        ["45"] = ("光标 - 下一单词", "Ctrl + Right"),
        ["46"] = ("选择 - 往上", "Shift + Up"),
        ["47"] = ("选择 - 往下", "Shift + Down"),
        ["48"] = ("选择 - 往左", "Shift + Left"),
        ["49"] = ("选择 - 往右", "Shift + Right"),
        ["50"] = ("选择 - 选到行首", "Shift + Home"),
        ["51"] = ("选择 - 选到行尾", "Shift + End"),
        ["52"] = ("选择 - 上一单词", "Ctrl+Shift+Left"),
        ["53"] = ("选择 - 下一单词", "Ctrl+Shift+Right"),
        ["54"] = ("右键菜单", "Menu key"),
        ["55"] = ("删除一行文本", "Delete a line"),
        ["56"] = ("删除一个单词", "Delete a word"),
        ["57"] = ("Shift 键", "Shift"),
        ["58"] = ("Ctrl 键", "Ctrl"),
        ["59"] = ("Alt 键", "Alt"),
        ["60"] = ("Win 键", "Win"),
        ["61"] = ("中英文之间加空格", "中英文间加空格"),
        ["62"] = (null, "Esc"),
        ["63"] = (null, "Backspace"),
        ["64"] = (null, "Enter"),
        ["65"] = (null, "Delete"),
        ["66"] = (null, "Insert"),
        ["67"] = (null, "Tab"),
        ["68"] = (null, "Ctrl + Tab"),
        ["69"] = (null, "Shift + Tab"),
        ["70"] = (null, "Ctrl+Shift+Tab"),

        // ---- MyKeymap ----
        ["71"] = ("暂停 MyKeymap", "Pause"),
        ["72"] = ("重启 MyKeymap", "Reload"),
        ["73"] = ("退出 MyKeymap", "Exit"),
        ["74"] = ("打开 MyKeymap 设置", "Settings"),
        ["75"] = ("触发 Abbreviation", "Abbreviation"),
        ["76"] = ("触发 Command", "Command"),
        ["77"] = ("切换 CapsLock 大小写", "CapsLock"),
        ["78"] = ("锁定当前模式 (免去按住触发键)", "Hold down/release the modifier key"),

        // ---- Action types ----
        ["200"] = ("⛔ 未配置", "⛔ Undefined"),
        ["201"] = ("🚀 启动程序或激活窗口", "🚀 App Launcher"),
        ["202"] = ("🖥️ 系统控制", "🖥️ System"),
        ["203"] = ("🏠 窗口操作", "🏠 Window"),
        ["204"] = ("🖱️  鼠标操作", "🖱️  Mouse"),
        ["205"] = ("🅰️ 重映射按键", "🅰️ Remap Keys"),
        ["206"] = ("🅰️ 输入按键或文本", "🅰️ Send Keys"),
        ["207"] = ("📚 文字编辑相关", "📚 Edit"),
        ["208"] = ("⚛️ 自定义函数", "⚛️ Custom Functions"),
        ["209"] = ("⚙️ MyKeymap 相关", "⚙️ MyKeymap"),

        // ---- App Launcher ----
        ["301"] = ("要激活的窗口 (进程匹配用 ahk_exe 程序名.exe)", "The window to activate (use ahk_exe for processes)"),
        ["301err"] = ("以 .exe 结尾会被当作窗口标题匹配而永远失败, 应写 ahk_exe 程序名.exe", "A trailing .exe is treated as a window title and never matches. Use ahk_exe app.exe"),
        ["301hint"] = ("窗口标题会变化的软件 (如聊天软件频道页) 不要用标题匹配, 应使用 ahk_exe 进程匹配", "Apps with changing titles (e.g. chat channels) should use ahk_exe, not a title match"),
        ["302"] = ("当窗口不存在时要启动的: 程序 / 文件夹 / URL", "Target"),
        ["303"] = ("命令行参数", "Arguments"),
        ["304"] = ("工作目录", "Working directory"),
        ["305"] = ("备注", "Comment"),
        ["306"] = ("以管理员运行", "Admin"),
        ["307"] = ("后台运行", "Hide"),
        ["308"] = ("检测隐藏窗口", "Detect hidden windows"),
        ["309"] = ("🔍 查看窗口标识符", "🔍 Window Spy"),

        // ---- Other ----
        ["401"] = ("重映射为", "Remap to"),
        ["402"] = ("要输入的按键或文本", "Keys"),
        ["403"] = ("代码", "Code"),
        ["404"] = ("热键", "Hotkey"),
        ["405"] = ("新增一个", "Add"),
        ["406"] = ("输入ab按回车添加/切换到ab, del ab删除ab, rn cd重命名当前为cd", "Input 'ab' and Enter to add a new item. Delete => del ab. Rename => rn cd."),

        // ---- Settings ----
        ["501"] = ("名称", "Name"),
        ["502"] = ("触发键", "Modifer"),
        ["503"] = ("上层", "Parent"),
        ["504"] = ("开关", ""),
        ["505"] = ("其他设置", "Other"),
        ["506"] = ("开机自启", "Run on system startup"),
        ["507"] = ("保存配置（CTRL+S）", "Save（CTRL+S）"),

        // ---- Window Groups ----
        ["601"] = ("😺 编辑程序分组", "😺 Window Groups"),
        ["602"] = ("组名", "Group Name"),
        ["603"] = ("窗口标识符", "Window List"),
        ["604"] = ("条件", "Condition"),
        ["605"] = ("是前台窗口", "is active"),
        ["606"] = ("这些窗口存在", "exist"),
        ["607"] = ("不是前台窗口", "not active"),
        ["608"] = ("这些窗口不存在", "not exist"),
        ["609"] = ("添加一行", "Add"),
        ["610"] = ("保存", "Save"),
        ["611"] = ("取消", "Cancel"),
        ["612"] = ("注意: MyKeyamp 的所有热键在 Exclude 组中会被禁用", "Note: MyKeymap will be disabled in the \"Exclude\" group."),

        // ---- Mouse Options ----
        ["701"] = ("🖱️ 修改鼠标参数", "🖱️ Mouse Options"),
        ["702"] = ("鼠标移动相关参数", "Mouse Options"),
        ["703"] = ("进入连续移动前的延时(秒)", "Delay 1"),
        ["704"] = ("两次移动的间隔时间(秒)", "Delay 2"),
        ["705"] = ("快速模式步长(像素)", "Length 1"),
        ["706"] = ("快速模式首步长(像素)", "Length 2"),
        ["707"] = ("慢速模式步长(像素)", "Length 3"),
        ["708"] = ("慢速模式首步长(像素)", "Length 4"),
        ["709"] = ("鼠标模式的提示符", "Prompt"),
        ["710"] = ("提示进入了鼠标模式", "Show prompt"),
        ["711"] = ("点击鼠标后不退出鼠标模式", "Use only the space key to exit"),
        ["712"] = ("滚轮相关参数", "Scroll Wheel Options"),
        ["713"] = ("进入连续滚动前的延时 (秒)", "Delay 1"),
        ["714"] = ("两次滚动的间隔时间 (越小滚动速度越快)", "Delay 2"),
        ["715"] = ("一次滚动的行数", "Lines to scroll at a time"),

        // ---- Keyboard Layout ----
        ["721"] = ("⌨️ 修改键盘布局", "⌨️ Keyboard Layout"),
        ["722"] = ("键盘布局", "Keyboard Layout"),
        ["723"] = ("重置为默认值", "Default"),
        ["724"] = ("重置为 74 键", "74 Key"),
        ["725"] = ("重置为 104 键", "104 Key"),
        ["726"] = ("添加鼠标按钮", "Add Mouse Buttons"),

        // ---- Command Window ----
        ["741"] = ("✨ 命令框皮肤", "✨ Command Window"),
        ["742"] = ("命令框皮肤", "Command Window"),
        ["743"] = ("窗口宽度", "Width"),
        ["744"] = ("窗口 Y 轴位置 (百分比)", "Y pos"),
        ["745"] = ("窗口圆角大小", "Border radius"),
        ["746"] = ("窗口动画持续时间", "Animation duration"),
        ["747"] = ("窗口背景色", "Background color"),
        ["748"] = ("透明度", "Opacity"),
        ["749"] = ("网格线颜色", "Gridline color"),
        ["750"] = ("边框宽度", "Border width"),
        ["751"] = ("边框颜色", "Border color"),
        ["752"] = ("按键颜色", "Key color"),
        ["753"] = ("四角颜色", "Corner color"),
        ["754"] = ("窗口阴影大小", "Drop shadow size"),
        ["755"] = ("窗口阴影颜色", "Drop shadow color"),

        // ---- Modifer Delay ----
        ["761"] = ("🕗 设置触发延时", "🕗 Modifer Delay"),
        ["762"] = ("触发延时 (毫秒)", "Modifer Delay (ms)"),
        ["763"] = ("一般推荐设为 0，让模式立刻生效。", "If delay > 0, you need long press to use the keymap."),
        ["764"] = ("如果设置大于零的值，即通过长按触发模式，也许能减少打字误触。", ""),
        ["765"] = ("但会有另一种形式的误触，比如想输入热键，但长按时间不够，所以触发热键失败。", ""),

        ["781"] = ("🌐 切换语言", "🌐 Change Language"),

        // ====================================================================
        // 900+: Avalonia 移植新增文案 (Vue 侧对应入口被注释或属于 config_doc 引导)
        // ====================================================================
        ["901"] = ("隐藏按键矩阵", "Hide key matrix"),
        ["902"] = ("隐藏悬浮按键矩阵窗口 (headless 场景/低打扰需求)", "Hide the floating key-matrix window"),
        ["903"] = ("📝 帮助页面", "📝 Help Page"),
        ["904"] = ("帮助页面 (HTML)", "Help Page (HTML)"),
        ["905"] = ("填写自定义 HTML 帮助页, 保存后生成 bin/site/help.html, 可通过 MyKeymap 动作 openHelpHtml 打开; 清空后删除生成的文件", "Custom HTML help page. On save it generates bin/site/help.html, opened by the openHelpHtml action. Clearing it deletes the file."),
        ["906"] = ("<h1>我的使用说明</h1>", "<h1>My usage notes</h1>"),
        ["907"] = ("🧩 编辑路径变量", "🧩 Path Variables"),
        ["908"] = ("编辑路径变量", "Path Variables"),
        ["909"] = ("变量名", "Name"),
        ["910"] = ("路径", "Path"),
        ["911"] = ("先定义一个 programs 变量，值为 C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\，然后就能在「程序路径」中使用 ahk-expression: programs \"Microsoft Edge.lnk\" 表示完整路径", "Define a 'programs' variable pointing to the Start Menu Programs folder, then use ahk-expression: programs \"Microsoft Edge.lnk\" in action paths."),
        ["912"] = ("删除", "Delete"),
        ["913"] = ("总览", "Overview"),
        ["914"] = ("选中动作", "Selected Action"),
        ["915"] = ("快捷键方案", "Keymaps"),
        ["916"] = ("设置", "Settings"),
        ["917"] = ("连接后端中…", "Connecting to backend…"),
        ["918"] = ("加载配置中…", "Loading config…"),
        ["919"] = ("无法连接到 MyKeymap 设置后端", "Cannot reach the MyKeymap settings backend"),
        ["920"] = ("重试", "Retry"),
        ["921"] = ("保存成功", "Saved"),
        ["922"] = ("保存失败，可能设置程序被关了", "Save failed, the settings server may have exited"),
        ["923"] = ("该页面将在后续移植任务中提供", "This page will be provided by a later migration task"),
        ["924"] = ("占位页", "Placeholder"),
        ["925"] = ("快捷键方案编辑页", "Keymap editor"),
        ["926"] = ("缩写命令编辑页", "Abbreviation editor"),
        ["927"] = ("选中动作方案编辑页", "Selected-action scheme editor"),
        ["928"] = ("已保存", "Saved"),
        ["929"] = ("保存", "Save"),
        ["930"] = ("开机自启开关需要 settings 服务调用系统接口, 失败时开关会还原", "The startup toggle calls a system API via the settings service; it reverts on failure"),
        ["931"] = ("总览页内容来自 config_doc.md (后端静态站点); 点击下方编辑区可自定义", "Overview content comes from config_doc.md (backend static site); click the edit zone below to customize"),
        ["932"] = ("帮助文档暂不可用 (后端未提供 config_doc.md)", "Help document unavailable (backend has no config_doc.md)"),
        ["933"] = ("新增", "Add"),
        ["934"] = ("MyKeymap 是一个按键层工具: 按住触发键进入某个模式, 该模式下普通按键被映射为自定义动作。", "MyKeymap is a key-layer tool: hold a modifier key to enter a layer where plain keys are remapped to custom actions."),
        ["935"] = ("快速上手", "Quick start"),
        ["936"] = ("左侧导航列出已启用的快捷键方案, 点击进入对应方案 (页面编辑功能由后续移植任务提供)。", "The left navigation lists enabled keymaps. Click one to open it (editing arrives in later migration tasks)."),
        ["937"] = ("在「设置」页可调整鼠标参数、命令框皮肤、触发延时、键盘布局、开机自启等。", "On the Settings page you can tune mouse options, command-window skin, modifier delay, keyboard layout, startup, etc."),
        ["938"] = ("按 Ctrl+S 或点击左下角「保存配置」把改动写回 config.json。", "Press Ctrl+S or click Save to write changes back to config.json."),
        ["939"] = ("文档", "Documentation"),
        ["950"] = ("开启时不允许删除", "Cannot delete while enabled"),
        ["951"] = ("被依赖时不允许删除", "Cannot delete while referenced as parent"),
        ["952"] = ("程序分组编辑在后续移植任务中提供", "Window-group editing arrives in a later migration task"),
        ["953"] = ("新增一个", "Add"),
        ["954"] = ("当前键不支持「 重映射 」，请使用「 输入文本或按键 」", "This key does not support Remap. Use \"Send Keys\" instead."),
        ["955"] = ("(1) 自定义函数可放到 data\\custom_functions.ahk，然后在此处调用", "(1) Put custom functions in data\\custom_functions.ahk, then call them here."),
        ["956"] = ("(2) 复杂的脚本推荐做成独立的 ahk 文件，然后用 MyKeymap 启动那个 ahk 文件", "(2) For complex scripts, make a standalone .ahk file and let MyKeymap launch it."),
        ["957"] = ("此处修改", "Edit here"),
        ["958"] = ("更多特殊按键参考:", "More special keys:"),

        // ====================================================================
        // 959+: 选中动作模块移植新增 (对应 Vue SelectedAction.vue / SelectedActionEdit.vue
        // 及 components/action/* 的硬编码文案)
        // ====================================================================
        ["959"] = ("新建方案", "New Scheme"),
        ["960"] = ("在任意窗口选中文本或文件后按下方案的快捷键, 即可触发预设行为。变量 %selected% 表示当前选中的内容。", "Select text or files in any window, then press the scheme's hotkey to trigger its action. %selected% stands for the selection."),
        ["961"] = ("(未命名)", "(Untitled)"),
        ["962"] = ("(未设置快捷键)", "(No hotkey)"),
        ["963"] = ("条规则", "rule(s)"),
        ["964"] = ("启用", "On"),
        ["965"] = ("关闭", "Off"),
        ["966"] = ("编辑", "Edit"),
        ["967"] = ("删除", "Delete"),
        ["968"] = ("删除方案", "Delete Scheme"),
        ["969"] = ("确定删除方案「{0}」吗? 此操作会立即保存并重启 MyKeymap。", "Delete scheme \"{0}\"? This saves immediately and restarts MyKeymap."),
        ["970"] = ("取消", "Cancel"),
        ["971"] = ("还没有选中动作方案", "No selected-action schemes yet"),
        ["972"] = ("点击右上角「新建方案」创建第一个方案", "Click \"New Scheme\" at the top right to create the first one"),
        ["973"] = ("创建失败, 请确认设置程序正在运行: ", "Create failed, make sure the settings server is running: "),
        ["974"] = ("创建失败", "Create failed"),
        ["975"] = ("方案名称", "Scheme name"),
        ["976"] = ("尚未设置快捷键, 保存后方案不会生效。", "No hotkey is set; the scheme won't take effect after saving."),
        ["977"] = ("规则列表", "Rules"),
        ["978"] = ("匹配优先级: 从上到下, 第一个命中的规则生效", "Matching priority: top to bottom, first match wins"),
        ["979"] = ("「默认 (兜底)」规则之后的规则永远不会被匹配, 请把默认规则移到最后", "Rules after the \"default\" rule never match; move the default rule to the end"),
        ["980"] = ("添加规则", "Add Rule"),
        ["981"] = ("暂无规则", "No rules"),
        ["982"] = ("点击左侧「添加规则」开始配置", "Click \"Add Rule\" on the left to start"),
        ["983"] = ("保存", "Save"),
        ["984"] = ("导出", "Export"),
        ["985"] = ("导入", "Import"),
        ["986"] = ("返回", "Back"),
        ["987"] = ("模拟测试", "Simulation Test"),
        ["988"] = ("模拟选中内容 (文本或文件路径, 多文件用换行分隔)", "Simulated selection (text or file paths; separate files with newlines)"),
        ["989"] = ("模拟文件选中", "Treat content as files"),
        ["990"] = ("测试", "Test"),
        ["991"] = ("请输入模拟选中内容", "Please enter simulated content"),
        ["992"] = ("测试失败: ", "Test failed: "),
        ["993"] = ("没有匹配到任何规则", "No rule matched"),
        ["994"] = ("命中的规则", "Matched rule"),
        ["995"] = ("优先级", "priority"),
        ["996"] = ("执行预览", "Execution preview"),
        ["997"] = ("(无预览)", "(no preview)"),
        ["998"] = ("任意内容", "Any content"),
        ["999"] = ("(未设置)", "(not set)"),
        ["1000"] = ("搜索: ", "Search: "),
        ["1001"] = ("按键: ", "Keys: "),
        ["1002"] = ("复制: ", "Copy: "),
        ["1003"] = ("(未设置行为)", "(no action)"),
        ["1004"] = ("匹配类型", "Match Type"),
        ["1005"] = ("条件值", "Condition Value"),
        ["1006"] = ("常用分组快捷填入", "File-group Quick Fill"),
        ["1007"] = ("选择分组自动填入后缀列表, 可继续手改; 选「无」清空条件值", "Picking a group auto-fills the extension list (still editable); \"None\" clears the value"),
        ["1008"] = ("无", "None"),
        ["1009"] = ("文本特征", "Text Feature"),
        ["1010"] = ("行为将随特征联动: ", "Actions follow the feature: "),
        ["1011"] = ("行为类型", "Action Type"),
        ["1012"] = ("目标命令 / URL / 脚本", "Target command / URL / script"),
        ["1013"] = ("该行为直接作用于选中内容", "This action applies directly to the selection"),
        ["1014"] = (", 无需配置命令模板。", "; no command template needed."),
        ["1015"] = ("工作目录 (可选)", "Working directory (optional)"),
        ["1016"] = ("执行后复制选中内容到剪贴板", "Copy the selection to the clipboard after running"),
        ["1017"] = ("执行后清空选中", "Clear the selection after running"),
        ["1018"] = ("执行前显示确认提示", "Show a confirmation prompt before running"),
        ["1019"] = ("导入规则", "Import Rules"),
        ["1020"] = ("粘贴导出的方案 JSON, 将替换当前方案的规则集 (方案名称与快捷键保持不变)。", "Paste an exported scheme JSON to replace the current rules (name and hotkey stay unchanged)."),
        ["1021"] = ("方案 JSON", "Scheme JSON"),
        ["1022"] = ("JSON 格式不正确: 缺少 rules 数组", "Invalid JSON: missing rules array"),
        ["1023"] = ("第 {0} 条规则: 文本特征「{1}」与行为「{2}」不匹配, 无法导入", "Rule {0}: text feature \"{1}\" cannot combine with action \"{2}\", import rejected"),
        ["1024"] = ("JSON 解析失败: ", "JSON parse failed: "),
        ["1025"] = ("该快捷键与现有设置冲突", "This hotkey conflicts with an existing one"),
        ["1026"] = ("请按下快捷键...", "Press the hotkey..."),
        ["1027"] = ("点击后按下快捷键", "Click, then press the hotkey"),
        ["1028"] = ("新的选中动作", "New Selected Action"),
        ["1029"] = ("返回列表", "Back to list"),
        ["1030"] = ("方案不存在", "Scheme not found"),
        // ---- 选中动作词表标签 (对应 constants.ts 的 MATCH_TYPES / ACTION_TYPES / TEXT_TYPES) ----
        ["1031"] = ("文件后缀", "File Extensions"),
        ["1032"] = ("文本特征", "Text Feature"),
        ["1033"] = ("默认 (兜底)", "Default (Fallback)"),
        ["1034"] = ("如 .txt 或 txt,md (逗号分隔多个), * 匹配任意文件", "e.g. .txt or txt,md (comma-separated); * matches any file"),
        ["1035"] = ("url (链接) / path (路径) / magnet (磁力链接) / plain (纯文本)", "url / path / magnet / plain"),
        ["1036"] = ("任何内容都匹配, 一般放在规则列表最后", "Matches anything; usually put at the end of the rule list"),
        ["1037"] = ("默认浏览器打开网址", "Open URL in Default Browser"),
        ["1038"] = ("打开文件/程序 (系统关联)", "Open File (System Association)"),
        ["1039"] = ("打开文件夹", "Open Folder"),
        ["1040"] = ("磁力链接下载", "Magnet Download"),
        ["1041"] = ("注册表定位", "Locate in Registry"),
        ["1042"] = ("程序打开", "Open with Program"),
        ["1043"] = ("搜索", "Search"),
        ["1044"] = ("执行命令", "Run Command"),
        ["1045"] = ("发送按键", "Send Keys"),
        ["1046"] = ("AHK 脚本", "AHK Script"),
        ["1047"] = ("复制", "Copy"),
        ["1048"] = ("用系统默认浏览器打开选中的网址, 不硬编码具体浏览器", "Open the selected URL with the system default browser"),
        ["1049"] = ("按系统关联程序打开选中的路径, 等同资源管理器双击", "Open the path with its associated program, like double-clicking in Explorer"),
        ["1050"] = ("用系统默认文件管理器打开选中路径所在文件夹", "Open the containing folder with the default file manager"),
        ["1051"] = ("用系统默认 BT 下载工具下载选中磁力链接 (走 magnet: 协议关联, 不依赖具体软件)", "Download with the system default BT client via the magnet: protocol"),
        ["1052"] = ("打开注册表编辑器并自动定位到选中的键路径", "Open regedit and jump to the selected key path"),
        ["1053"] = ("用指定程序打开选中文件, 如 code.exe %selected%", "Open the selected file with a program, e.g. code.exe %selected%"),
        ["1054"] = ("把 %selected% 作为关键词搜索, 如 https://www.google.com/search?q=%selected%", "Use %selected% as the search keyword"),
        ["1055"] = ("执行命令, 支持 %selected% 变量, 如 notepad.exe %selected%", "Run a command with %selected% substituted"),
        ["1056"] = ("向当前窗口发送按键序列, 如 ^c; 含选中内容时建议用 {text}%selected% 包裹, 否则内容中的 {Enter} 等会被解析为按键", "Send keys to the current window; wrap %selected% with {text} so braces aren't parsed as keys"),
        ["1057"] = ("执行一段 AutoHotkey 脚本片段, %selected% 会替换为字符串字面量", "Run an AHK snippet; %selected% becomes a string literal"),
        ["1058"] = ("把 %selected% 替换后的文本写入剪贴板", "Write the substituted text to the clipboard"),
        ["1059"] = ("链接", "URL"),
        ["1060"] = ("路径", "Path"),
        ["1061"] = ("磁力链接", "Magnet Link"),
        ["1062"] = ("纯文本", "Plain Text"),
        ["1063"] = ("快捷键", "Hotkey"),
    };
}

/// <summary>
/// 绑定转换器: ConverterParameter 为文案键, 绑定源为 ViewModel 的 LanguageTick。
/// LanguageTick 递增 -> 绑定重新求值 -> 按当前语言重新翻译。
/// </summary>
public sealed class I18nConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => I18n.T(parameter as string ?? parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
