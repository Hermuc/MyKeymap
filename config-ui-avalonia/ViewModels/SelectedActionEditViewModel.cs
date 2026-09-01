using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.Services;

namespace MyKeymap.Settings.ViewModels;

/// <summary>
/// 规则列表条目 (复刻 RuleList.vue 的 v-list-item):
/// 展示「匹配类型 → 条件值 / 行为类型 · 命令」两行文本, 带上移/下移/删除操作;
/// 测试命中时以优先级匹配高亮 (复刻 matched-rule 样式)。
/// </summary>
public sealed partial class RuleItemVm : ObservableObject
{
    public RuleItemVm(ActionRule rule, int index)
    {
        Rule = rule;
        Index = index;
        RefreshDisplay();
    }

    public ActionRule Rule { get; }

    /// <summary>列表位置 (0 起); 展示序号为 Index+1 (复刻 chip {{ index + 1 }})。</summary>
    public int Index { get; internal set; }

    public string IndexText => (Index + 1).ToString();

    // ---- 展示文本 (复刻 RuleList.vue 的 label/text 函数) ----

    public string MatchTypeLabel { get; private set; } = "";
    public string MatchValueText { get; private set; } = "";
    public string ActionTypeLabel { get; private set; } = "";
    public string Subtitle { get; private set; } = "";

    /// <summary>命中高亮底色 (复刻 .matched-rule 的 rgba(65,105,225,.12))。</summary>
    [ObservableProperty]
    private string _rowBackground = "Transparent";

    /// <summary>按当前规则内容与语言重算展示文本。</summary>
    public void RefreshDisplay()
    {
        MatchTypeLabel = ActionSchemeCatalog.MatchTypeLabel(Rule.MatchType);
        MatchValueText = Rule.MatchType == "default"
            ? I18n.T("998")
            : (string.IsNullOrEmpty(Rule.MatchValue) ? I18n.T("999") : Rule.MatchValue);
        ActionTypeLabel = ActionSchemeCatalog.ActionTypeLabel(Rule.ActionType);

        var actionText = ActionValueText();
        Subtitle = $"{ActionTypeLabel} · {(string.IsNullOrEmpty(actionText) ? I18n.T("1003") : actionText)}";

        OnPropertyChanged(nameof(MatchTypeLabel));
        OnPropertyChanged(nameof(MatchValueText));
        OnPropertyChanged(nameof(ActionTypeLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IndexText));
    }

    private string ActionValueText()
    {
        // 复刻 actionValueText
        if (Rule.ActionType == "search") return I18n.T("1000") + Rule.ActionValue;
        if (Rule.ActionType == "send_keys") return I18n.T("1001") + Rule.ActionValue;
        if (Rule.ActionType == "copy") return I18n.T("1002") + Rule.ActionValue;
        if (ActionSchemeCatalog.TextActions.Contains(Rule.ActionType))
        {
            return ActionSchemeCatalog.ActionTypeLabel(Rule.ActionType);
        }
        return Rule.ActionValue;
    }

    /// <summary>测试命中高亮 (优先级相等时生效, 复刻 matchedPriority 语义)。</summary>
    public void SetMatched(int? matchedPriority)
        => RowBackground = matchedPriority is int p && p == Rule.Priority ? "#E3EAFB" : "Transparent";

    [RelayCommand]
    private void MoveUp() => OwnerRef?.MoveRule(Index, -1);

    [RelayCommand]
    private void MoveDown() => OwnerRef?.MoveRule(Index, 1);

    [RelayCommand]
    private void Delete() => OwnerRef?.DeleteRule(Index);

    /// <summary>所属编辑页 VM (由集合宿主回填, 避免模板里层层 Binding)。</summary>
    internal SelectedActionEditViewModel? OwnerRef { get; set; }
}

/// <summary>
/// 规则编辑器 (复刻 RuleEditor.vue): 绑定当前选中规则, 提供
/// 匹配类型 / 条件值 (文件后缀 + 分组快捷填入, 文本特征联动) / 行为类型 /
/// 命令模板 / 工作目录 / 三个选项开关 的完整交互。
/// 每次切换选中规则时由宿主重建实例 (避免复杂重绑定)。
/// </summary>
public sealed partial class RuleEditorVm : ObservableObject
{
    private readonly SelectedActionEditViewModel _owner;
    private bool _applying; // 程序化回填时抑制联动处理器

    public RuleEditorVm(SelectedActionEditViewModel owner, ActionRule rule)
    {
        _owner = owner;
        Rule = rule;

        MatchTypeOptions = ActionSchemeCatalog.MatchTypes
            .Select(t => new ComboOption(t.Value, I18n.T(t.LabelKey))).ToList();
        TextTypeOptions = ActionSchemeCatalog.TextTypes
            .Select(t => new ComboOption(t.Value, I18n.T(t.LabelKey))).ToList();

        var groups = new List<ComboOption> { new("", I18n.T("1008")) }; // 首位固定「无」
        groups.AddRange(owner.FileGroups.Select(g => new ComboOption(g.Name, g.Label)));
        FileGroupOptions = groups;

        ApplyFromRule();
    }

    public ActionRule Rule { get; }

    // ---- 表单标签 (预翻译副本; 语言切换时由宿主重建编辑器实例, 无需单独刷新) ----
    public string LblMatchType => I18n.T("1004");
    public string LblMatchValue => I18n.T("1005");
    public string LblFileGroup => I18n.T("1006");
    public string LblTextType => I18n.T("1009");
    public string LblActionType => I18n.T("1011");
    public string LblActionValue => I18n.T("1012");
    public string LblWorkingDir => I18n.T("1015");
    public string LblCopy => I18n.T("1016");
    public string LblClear => I18n.T("1017");
    public string LblConfirm => I18n.T("1018");

    public IReadOnlyList<ComboOption> MatchTypeOptions { get; }
    public IReadOnlyList<ComboOption> TextTypeOptions { get; }
    public IReadOnlyList<ComboOption> FileGroupOptions { get; }

    // ------------------------------------------------------------- 匹配类型

    [ObservableProperty]
    private ComboOption _selectedMatchType = new("fileExt", "");

    /// <summary>切换匹配类型时重置条件值 (复刻: textType→url, default→*, 其余→"")。</summary>
    partial void OnSelectedMatchTypeChanged(ComboOption value)
    {
        if (_applying || value.Value == Rule.MatchType) return;
        Rule.MatchType = value.Value;
        Rule.MatchValue = value.Value switch
        {
            "textType" => "url",
            "default" => "*",
            _ => "",
        };
        FileGroupSelected = null; // 复位快捷填入 (复刻 watch(matchType))
        ApplyFromRule();
        _owner.OnRuleEdited();
    }

    /// <summary>条件值输入框可见性 (仅文件后缀; 复刻 showMatchValueInput)。</summary>
    public bool ShowMatchValueInput => Rule.MatchType == "fileExt";

    /// <summary>文本特征下拉可见性。</summary>
    public bool ShowTextTypeSelect => Rule.MatchType == "textType";

    /// <summary>文件分组快捷填入可见性 (复刻: fileExt && fileGroups.length > 0)。</summary>
    public bool ShowFileGroupFill => Rule.MatchType == "fileExt" && _owner.FileGroups.Count > 0;

    public string MatchHint => ActionSchemeCatalog.MatchTypeHint(Rule.MatchType);

    /// <summary>条件值 (文件后缀的逗号分隔列表, 可手改)。</summary>
    public string MatchValue
    {
        get => Rule.MatchValue;
        set
        {
            if (Rule.MatchValue == value) return;
            Rule.MatchValue = value;
            OnPropertyChanged(nameof(MatchValue));
            _owner.OnRuleEdited();
        }
    }

    // ------------------------------------------------------------- 文本特征

    [ObservableProperty]
    private ComboOption _selectedTextType = new("url", "");

    /// <summary>
    /// 切换文本特征 (复刻 onTextTypeChange): 若当前行为不在新特征可选范围内,
    /// 自动落到默认行为; 纠正到无参行为时清空命令模板。
    /// </summary>
    partial void OnSelectedTextTypeChanged(ComboOption value)
    {
        if (_applying || value.Value == Rule.MatchValue) return;
        var allowed = ActionSchemeCatalog.TextTypeActions.GetValueOrDefault(value.Value) ?? [];
        var actionType = Rule.ActionType;
        if (!allowed.Contains(actionType))
        {
            actionType = ActionSchemeCatalog.TextTypeDefaultAction.GetValueOrDefault(value.Value) ?? "";
        }
        Rule.MatchValue = value.Value;
        Rule.ActionType = actionType;
        if (ActionSchemeCatalog.TextActions.Contains(actionType))
        {
            Rule.ActionValue = ""; // 避免残留模板误导
        }
        ApplyFromRule();
        _owner.OnRuleEdited();
    }

    /// <summary>文本特征联动提示 (复刻: 行为将随特征联动: 链接/路径/...)。</summary>
    public string TextTypeHint => I18n.T("1010") + ActionSchemeCatalog.TextTypeLabel(Rule.MatchValue);

    // ------------------------------------------------------------- 文件分组快捷填入

    /// <summary>
    /// 快捷填入选中项: null=未选 (占位符) / ""=「无」/ 分组 name。
    /// 复刻 RuleEditor.vue 的 fileGroupSelected 三态语义。
    /// </summary>
    [ObservableProperty]
    private ComboOption? _fileGroupSelected;

    partial void OnFileGroupSelectedChanged(ComboOption? value)
    {
        if (_applying) return;
        if (value is null) return; // 清除 (×) 或程序化复位: 保留条件值供手改
        if (value.Value == "")
        {
            MatchValue = ""; // 选中「无」: 清空条件值
        }
        else
        {
            var group = _owner.FileGroups.FirstOrDefault(g => g.Name == value.Value);
            if (group is not null) MatchValue = string.Join(", ", group.Exts);
        }
    }

    public string FileGroupHint => I18n.T("1007");

    // ------------------------------------------------------------- 行为类型

    /// <summary>行为下拉选项: textType 时随特征动态联动, 其余匹配类型展示全量。</summary>
    public IReadOnlyList<ComboOption> ActionTypeOptions
    {
        get
        {
            var all = ActionSchemeCatalog.ActionTypes;
            if (Rule.MatchType != "textType")
            {
                return all.Select(t => new ComboOption(t.Value, I18n.T(t.LabelKey))).ToList();
            }
            var allowed = ActionSchemeCatalog.TextTypeActions.GetValueOrDefault(Rule.MatchValue) ?? [];
            return all.Where(t => allowed.Contains(t.Value))
                .Select(t => new ComboOption(t.Value, I18n.T(t.LabelKey))).ToList();
        }
    }

    [ObservableProperty]
    private ComboOption _selectedActionType = new("open", "");

    /// <summary>切换行为类型时给出默认模板 (复刻: 无参行为清空; search 补默认搜索; run 补 %selected%)。</summary>
    partial void OnSelectedActionTypeChanged(ComboOption value)
    {
        if (_applying || value.Value == Rule.ActionType) return;
        var v = Rule.ActionValue;
        if (ActionSchemeCatalog.TextActions.Contains(value.Value))
        {
            v = "";
        }
        else if (value.Value == "search" && v == "")
        {
            v = ActionSchemeCatalog.DefaultSearchUrl;
        }
        else if (value.Value == "run" && v == "")
        {
            v = "%selected%";
        }
        Rule.ActionType = value.Value;
        Rule.ActionValue = v;
        ApplyFromRule();
        _owner.OnRuleEdited();
    }

    /// <summary>是否文本特征专用行为 (无命令模板, 复刻 isTextAction)。</summary>
    public bool IsTextAction => ActionSchemeCatalog.TextActions.Contains(Rule.ActionType);

    public bool ShowActionValue => !IsTextAction;

    /// <summary>无参行为提示 (复刻: 该行为直接作用于选中内容 (hint), 无需配置命令模板)。</summary>
    public string TextActionInfo =>
        $"{I18n.T("1013")} ({ActionSchemeCatalog.ActionTypeHint(Rule.ActionType)}){I18n.T("1014")}";

    public string ActionHint => ActionSchemeCatalog.ActionTypeHint(Rule.ActionType);

    public string ActionValue
    {
        get => Rule.ActionValue;
        set
        {
            if (Rule.ActionValue == value) return;
            Rule.ActionValue = value;
            OnPropertyChanged(nameof(ActionValue));
            _owner.OnRuleEdited();
        }
    }

    // ------------------------------------------------------------- 工作目录与选项

    public bool ShowWorkingDir => Rule.ActionType == "run";

    public string WorkingDir
    {
        get => Rule.WorkingDir;
        set
        {
            if (Rule.WorkingDir == value) return;
            Rule.WorkingDir = value;
            OnPropertyChanged(nameof(WorkingDir));
        }
    }

    public bool CopyToClipboard
    {
        get => Rule.Options.CopyToClipboard;
        set { if (Rule.Options.CopyToClipboard == value) return; Rule.Options.CopyToClipboard = value; OnPropertyChanged(); }
    }

    public bool ClearSelection
    {
        get => Rule.Options.ClearSelection;
        set { if (Rule.Options.ClearSelection == value) return; Rule.Options.ClearSelection = value; OnPropertyChanged(); }
    }

    public bool Confirm
    {
        get => Rule.Options.Confirm;
        set { if (Rule.Options.Confirm == value) return; Rule.Options.Confirm = value; OnPropertyChanged(); }
    }

    // ------------------------------------------------------------- 回填

    /// <summary>按底层规则回填全部选择项与可见性 (程序化, 抑制联动)。</summary>
    private void ApplyFromRule()
    {
        _applying = true;
        try
        {
            SelectedMatchType = MatchTypeOptions.FirstOrDefault(o => o.Value == Rule.MatchType) ?? MatchTypeOptions[0];
            SelectedTextType = TextTypeOptions.FirstOrDefault(o => o.Value == Rule.MatchValue) ?? TextTypeOptions[0];
            SelectedActionType = ActionTypeOptions.FirstOrDefault(o => o.Value == Rule.ActionType)
                                 ?? ActionTypeOptions.FirstOrDefault()
                                 ?? new ComboOption("", "");
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(ShowMatchValueInput));
        OnPropertyChanged(nameof(ShowTextTypeSelect));
        OnPropertyChanged(nameof(ShowFileGroupFill));
        OnPropertyChanged(nameof(MatchHint));
        OnPropertyChanged(nameof(MatchValue));
        OnPropertyChanged(nameof(TextTypeHint));
        OnPropertyChanged(nameof(ActionTypeOptions));
        OnPropertyChanged(nameof(IsTextAction));
        OnPropertyChanged(nameof(ShowActionValue));
        OnPropertyChanged(nameof(TextActionInfo));
        OnPropertyChanged(nameof(ActionHint));
        OnPropertyChanged(nameof(ActionValue));
        OnPropertyChanged(nameof(ShowWorkingDir));
        OnPropertyChanged(nameof(WorkingDir));
    }
}

/// <summary>
/// 选中动作编辑页 (复刻 views/SelectedActionEdit.vue, 路由 /keymap/action/:id):
/// 方案名称/热键 (HotkeyCapture + 冲突检测)/启用开关; 规则列表 (优先级升序、增删排序、导入导出);
/// 规则编辑器; 模拟测试器 (编辑中快照随请求发出); 保存。
///
/// 保存路径照 Vue 源: 方案数据存于 config.actionSchemes, 「保存」按钮走
/// store.saveConfig() -> PUT /config (本移植为 MainViewModel.SaveAsync);
/// 仅「新建方案」在列表页走 POST /api/action-schemes。
/// </summary>
public sealed partial class SelectedActionEditViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly SelectedActionPageViewModel _page;

    public SelectedActionEditViewModel(MainViewModel main, SelectedActionPageViewModel page, ActionScheme scheme)
    {
        _main = main;
        _page = page;
        Scheme = scheme;
        UsedHotkeys = BuildUsedHotkeys();
        SyncRulesFromModel(0);
    }

    public ActionScheme Scheme { get; }
    public MainViewModel Main => _main;
    public Config Config => _main.Config ?? throw new InvalidOperationException("Config 未加载");
    private ISettingsApi? Api => _main.Session.Api;

    /// <summary>语言切换递增, 驱动页内 ConverterParameter 文案重算。</summary>
    [ObservableProperty]
    private int _languageTick;

    /// <summary>文件分组 (快捷填入数据源, 复刻 configStore.config?.fileGroups)。</summary>
    public IReadOnlyList<FileGroup> FileGroups => Config.FileGroups;

    // ------------------------------------------------------------- 方案字段

    public string Name
    {
        get => Scheme.Name;
        set
        {
            if (Scheme.Name == value) return;
            Scheme.Name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    public string Hotkey
    {
        get => Scheme.Hotkey;
        set
        {
            if (Scheme.Hotkey == value) return;
            Scheme.Hotkey = value;
            OnPropertyChanged(nameof(Hotkey));
            OnPropertyChanged(nameof(NoHotkeyWarning));
            UsedHotkeys = BuildUsedHotkeys();
            HotkeyPendingSave = true; // 未保存提示条 (保存成功后复位)
        }
    }

    public bool Enable
    {
        get => Scheme.Enable;
        set
        {
            if (Scheme.Enable == value) return;
            Scheme.Enable = value;
            OnPropertyChanged(nameof(Enable));
            OnPropertyChanged(nameof(EnableLabel));
        }
    }

    public string EnableLabel => Scheme.Enable ? I18n.T("964") : I18n.T("965");

    /// <summary>空热键警示 (复刻 !scheme.hotkey 的 warning alert)。</summary>
    public bool NoHotkeyWarning => string.IsNullOrEmpty(Scheme.Hotkey);

    /// <summary>
    /// 热键已修改但尚未保存 (轻量提示「保存后才生效」, 保存成功后复位):
    /// 触发键改完不点保存会被误认为「修改不生效」, 与全局配置共存的方案字段改动
    /// 只有经 PUT /config + 引擎重启才会真正写入热键。
    /// </summary>
    [ObservableProperty]
    private bool _hotkeyPendingSave;

    /// <summary>
    /// 已占用热键 (复刻 usedHotkeys 计算): 启用的 keymaps 全部热键 (归一化)
    /// + 其他方案热键 (仅去 *~$ 前缀并小写, 照 Vue 原样)。
    /// </summary>
    [ObservableProperty]
    private HashSet<string> _usedHotkeys = [];

    private HashSet<string> BuildUsedHotkeys()
    {
        var used = HotkeyLogic.CollectUsedHotkeys(Config.Keymaps);
        foreach (var s in Config.ActionSchemes)
        {
            if (s.Id != Scheme.Id && !string.IsNullOrEmpty(s.Hotkey))
            {
                used.Add(HotkeyLogic.StripWildcardPrefix(s.Hotkey));
            }
        }
        return used;
    }

    // ------------------------------------------------------------- 规则列表

    public ObservableCollection<RuleItemVm> Rules { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    private RuleItemVm? _selectedRule;

    partial void OnSelectedRuleChanged(RuleItemVm? value)
    {
        Editor = value is null ? null : new RuleEditorVm(this, value.Rule);
    }

    [ObservableProperty]
    private RuleEditorVm? _editor;

    public bool HasEditor => Editor is not null;

    /// <summary>
    /// 兜底规则不在末尾警示 (复刻 defaultRuleNotLast): default 之后还有其他规则时,
    /// 这些规则永远不会被匹配到 (第一个命中的规则生效)。
    /// </summary>
    [ObservableProperty]
    private bool _defaultRuleNotLast;

    private void RefreshDefaultWarning()
    {
        var idx = Scheme.Rules.FindIndex(r => r.MatchType == "default");
        DefaultRuleNotLast = idx >= 0 && idx < Scheme.Rules.Count - 1;
    }

    /// <summary>按模型重建规则条目集合 (保持/指定选中下标)。</summary>
    private void SyncRulesFromModel(int selectIndex)
    {
        Rules.Clear();
        for (var i = 0; i < Scheme.Rules.Count; i++)
        {
            var item = new RuleItemVm(Scheme.Rules[i], i) { OwnerRef = this };
            Rules.Add(item);
        }
        var clamped = Math.Clamp(selectIndex, 0, Rules.Count - 1);
        SelectedRule = clamped >= 0 ? Rules[clamped] : null;
        RefreshDefaultWarning();
        ReapplyMatchedHighlight();
    }

    /// <summary>添加规则 (复刻 addRule): priority = 现有最大值 + 1, 选中新增项。</summary>
    [RelayCommand]
    private void AddRule()
    {
        var maxPriority = Scheme.Rules.Count > 0 ? Scheme.Rules.Max(r => r.Priority) : 0;
        Scheme.Rules.Add(ActionSchemeCatalog.CreateRule(maxPriority + 1));
        SyncRulesFromModel(Scheme.Rules.Count - 1);
    }

    /// <summary>删除规则 (复刻 deleteRule): 选中下标越界时收敛到末尾。</summary>
    public void DeleteRule(int index)
    {
        if (index < 0 || index >= Scheme.Rules.Count) return;
        Scheme.Rules.RemoveAt(index);
        var next = Math.Min(index, Scheme.Rules.Count - 1);
        SyncRulesFromModel(next);
    }

    /// <summary>上移/下移 (复刻 moveRule): 交换数组位置, 选中跟随; 不重写 priority。</summary>
    public void MoveRule(int index, int dir)
    {
        var target = index + dir;
        if (index < 0 || index >= Scheme.Rules.Count || target < 0 || target >= Scheme.Rules.Count) return;
        (Scheme.Rules[index], Scheme.Rules[target]) = (Scheme.Rules[target], Scheme.Rules[index]);
        SyncRulesFromModel(target);
    }

    /// <summary>拖拽重排 (复刻 dropRule): 抽出后插入目标位置。</summary>
    public void DropRule(int from, int to)
    {
        if (from == to || from < 0 || from >= Scheme.Rules.Count) return;
        var rule = Scheme.Rules[from];
        Scheme.Rules.RemoveAt(from);
        to = Math.Clamp(to, 0, Scheme.Rules.Count);
        Scheme.Rules.Insert(to, rule);
        SyncRulesFromModel(to);
    }

    /// <summary>编辑器改了规则内容: 刷新列表展示文本与兜底警示。</summary>
    public void OnRuleEdited()
    {
        foreach (var item in Rules) item.RefreshDisplay();
        RefreshDefaultWarning();
    }

    // ------------------------------------------------------------- 模拟测试

    /// <summary>模拟选中内容 (复刻默认值 "https://example.com")。</summary>
    [ObservableProperty]
    private string _testContent = "https://example.com";

    [ObservableProperty]
    private bool _testIsFile;

    [ObservableProperty]
    private bool _testing;

    [ObservableProperty]
    private string _testError = "";

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _resultMatched;

    [ObservableProperty]
    private string _matchedRuleText = "";

    [ObservableProperty]
    private string _previewText = "";

    /// <summary>测试命中的规则优先级 (驱动规则列表高亮, 复刻 matched emit)。</summary>
    public int? MatchedPriority { get; private set; }

    /// <summary>
    /// 模拟测试 (复刻 ActionTester.runTest): 把编辑中未保存的方案快照随请求发出;
    /// 后端 400 时展示 message (方案级 400 无「保存失败: 」前缀)。
    /// </summary>
    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(TestContent))
        {
            TestError = I18n.T("991");
            return;
        }
        if (Api is null) return;

        Testing = true;
        TestError = "";
        HasResult = false;
        SetMatched(null);

        var resp = await Api.TestActionSchemeAsync(new ActionSchemeTestRequest
        {
            SchemeId = Scheme.Id,
            Scheme = Scheme, // 编辑中快照 (未保存修改也能测试)
            Content = TestContent,
            IsFile = TestIsFile,
        });

        Testing = false;
        if (!resp.Success)
        {
            TestError = I18n.T("992") + (resp.ErrorMessage ?? $"HTTP {resp.StatusCode}");
            return;
        }

        HasResult = true;
        var result = resp.Value;
        if (result is { Matched: true, Rule: not null })
        {
            ResultMatched = true;
            SetMatched(result.Rule.Priority);
            MatchedRuleText =
                $"{ActionSchemeCatalog.MatchTypeLabel(result.Rule.MatchType)}: {result.Rule.MatchValue}"
                + $" → {ActionSchemeCatalog.ActionTypeLabel(result.Rule.ActionType)}";
            PreviewText = string.IsNullOrEmpty(result.Preview) ? I18n.T("997") : result.Preview;
        }
        else
        {
            ResultMatched = false;
        }
    }

    private void SetMatched(int? priority)
    {
        MatchedPriority = priority;
        ReapplyMatchedHighlight();
    }

    private void ReapplyMatchedHighlight()
    {
        foreach (var item in Rules) item.SetMatched(MatchedPriority);
    }

    // ------------------------------------------------------------- 保存 / 删除 / 导入导出

    /// <summary>保存 (复刻「保存」按钮 -> store.saveConfig() -> PUT /config); 成功后复位热键未保存提示。</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var ok = await _page.SaveConfigAsync();
        if (ok) HotkeyPendingSave = false;
    }

    [RelayCommand]
    private void GoBack() => _page.ShowList();

    /// <summary>删除方案 (复刻编辑页 confirmDelete): 确认后移除 + 保存 + 回列表。</summary>
    [RelayCommand]
    private async Task AskDeleteSchemeAsync()
    {
        var name = string.IsNullOrEmpty(Scheme.Name) ? I18n.T("961") : Scheme.Name;
        var confirmed = _page.ConfirmAsync is not null
            ? await _page.ConfirmAsync(I18n.T("968"), string.Format(I18n.T("969"), name))
            : false;
        if (!confirmed) return;

        Config.ActionSchemes.Remove(Scheme);
        await _page.SaveConfigAsync();
        _page.ShowList();
    }

    /// <summary>导出 JSON (复刻 exportScheme); 实际文件保存由视图的文件选择器完成。</summary>
    public string BuildExportJson() => JsonSerializer.Serialize(Scheme, ExportJsonOptions);

    private static readonly JsonSerializerOptions ExportJsonOptions = new(SettingsJson.Options)
    {
        WriteIndented = true,
    };

    public string ExportFileName => $"action-scheme-{Scheme.Id}-{(string.IsNullOrEmpty(Scheme.Name) ? "unnamed" : Scheme.Name)}.json";

    /// <summary>
    /// 导入规则集 (复刻 confirmImport): 校验 rules 数组与 textType 组合合法性,
    /// 按数组顺序重写 priority; 保留方案的 id/name/hotkey/enable。
    /// 返回错误文案; 成功返回 null。
    /// </summary>
    public string? TryImport(string json)
    {
        List<ActionRule>? rules;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rules", out var rulesEl)
                || rulesEl.ValueKind != JsonValueKind.Array)
            {
                return I18n.T("1022");
            }
            rules = rulesEl.Deserialize<List<ActionRule>>(SettingsJson.Options);
            if (rules is null) return I18n.T("1022");
        }
        catch (JsonException e)
        {
            return I18n.T("1024") + e.Message;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r.MatchType != "textType") continue;
            var allowed = ActionSchemeCatalog.TextTypeActions.GetValueOrDefault(r.MatchValue) ?? [];
            if (!allowed.Contains(r.ActionType))
            {
                return string.Format(I18n.T("1023"), i + 1,
                    ActionSchemeCatalog.TextTypeLabel(r.MatchValue),
                    ActionSchemeCatalog.ActionTypeLabel(r.ActionType));
            }
        }

        // 按数组顺序重写 priority, 保证与匹配顺序一致
        for (var i = 0; i < rules.Count; i++) rules[i].Priority = i + 1;
        Scheme.Rules = rules;
        SetMatched(null);
        SyncRulesFromModel(0);
        return null;
    }

    /// <summary>语言切换: 重建规则展示文本与编辑器 (选项标签为预翻译副本)。</summary>
    public void OnLanguageChanged()
    {
        LanguageTick++;
        OnPropertyChanged(nameof(EnableLabel));
        foreach (var item in Rules) item.RefreshDisplay();
        if (SelectedRule is not null)
        {
            Editor = new RuleEditorVm(this, SelectedRule.Rule);
        }
        if (HasResult && ResultMatched)
        {
            // 结果文案为即时拼接, 触发重算
            OnPropertyChanged(nameof(MatchedRuleText));
        }
    }
}
