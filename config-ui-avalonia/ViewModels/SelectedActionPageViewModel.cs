using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyKeymap.Settings.Models;
using MyKeymap.Settings.Services;

namespace MyKeymap.Settings.ViewModels;

/// <summary>
/// 选中动作方案卡片 (复刻 SelectedAction.vue 的 v-card):
/// 名称 / 热键显示 / 规则数 / 启用开关 / 编辑删除入口; 整卡点击进入编辑页。
/// </summary>
public sealed partial class SchemeCardVm : ObservableObject
{
    private readonly SelectedActionPageViewModel _owner;

    public SchemeCardVm(SelectedActionPageViewModel owner, ActionScheme scheme)
    {
        _owner = owner;
        Scheme = scheme;
    }

    public ActionScheme Scheme { get; }

    /// <summary>名称, 空时显示「(未命名)」(复刻 scheme.name || "(未命名)")。</summary>
    public string NameDisplay => string.IsNullOrEmpty(Scheme.Name) ? I18n.T("961") : Scheme.Name;

    /// <summary>热键可读形式, 空时显示「(未设置快捷键)」。</summary>
    public string HotkeyDisplay =>
        string.IsNullOrEmpty(Scheme.Hotkey) ? I18n.T("962") : HotkeyLogic.AhkToDisplay(Scheme.Hotkey);

    public bool HasHotkey => !string.IsNullOrEmpty(Scheme.Hotkey);

    /// <summary>规则数摘要 (复刻 "{{ ruleCount(scheme) }} 条规则")。</summary>
    public string RuleCountText => $"{Scheme.Rules.Count} {I18n.T("963")}";

    /// <summary>启用状态指示色 (复刻图标 primary/grey 语义)。</summary>
    public string EnableDotColor => Scheme.Enable ? "#4169E1" : "#BDBDBD";

    public string EnableLabel => Scheme.Enable ? I18n.T("964") : I18n.T("965");

    /// <summary>卡片操作按钮文案 (预翻译, 避免模板内跨层绑定)。</summary>
    public string EditLabel => I18n.T("966");
    public string DeleteLabel => I18n.T("967");

    /// <summary>启用开关: 写入内存配置并立即保存 (复刻 toggleEnable -> saveConfig)。</summary>
    public bool Enable
    {
        get => Scheme.Enable;
        set
        {
            if (Scheme.Enable == value) return;
            Scheme.Enable = value;
            OnPropertyChanged(nameof(EnableLabel));
            OnPropertyChanged(nameof(EnableDotColor));
            _ = _owner.SaveConfigAsync();
        }
    }

    [RelayCommand]
    private void Open() => _owner.OpenEdit(Scheme);

    [RelayCommand]
    private void AskDelete() => _ = _owner.AskDeleteAsync(this);

    /// <summary>语言切换后刷新预翻译文本。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(NameDisplay));
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(RuleCountText));
        OnPropertyChanged(nameof(EnableLabel));
        OnPropertyChanged(nameof(EditLabel));
        OnPropertyChanged(nameof(DeleteLabel));
    }
}

/// <summary>
/// 选中动作列表页 (复刻 views/SelectedAction.vue, 路由 /keymap/action),
/// 同时承担编辑页宿主角色 (Vue 的 /keymap/action/:id 在此以页内切换实现)。
///
/// 保存路径照 Vue 源实现:
///   - 新建方案: POST /api/action-schemes (后端分配 id 并立即持久化), 成功后同步进
///     内存 Config.ActionSchemes (复刻 store.actionSchemes.push);
///   - 启用开关 / 删除: 改内存 Config 后走 PUT /config (复刻 store.saveConfig)。
/// </summary>
public sealed partial class SelectedActionPageViewModel : ObservableObject, ILanguageRefresh
{
    private readonly MainViewModel _main;

    public SelectedActionPageViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCards();
    }

    public MainViewModel Main => _main;
    public Config Config => _main.Config ?? throw new InvalidOperationException("Config 未加载");
    private ISettingsApi? Api => _main.Session.Api;

    /// <summary>语言切换递增, 驱动页内 ConverterParameter 文案重算。</summary>
    [ObservableProperty]
    private int _languageTick;

    public ObservableCollection<SchemeCardVm> Cards { get; } = [];

    /// <summary>当前编辑页 VM; null 时为列表态 (复刻 router 两级路由)。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private SelectedActionEditViewModel? _editVm;

    public bool IsListMode => EditVm is null;
    public bool IsEditMode => EditVm is not null;

    public bool HasCards => Cards.Count > 0;

    /// <summary>确认对话框委托 (视图注入真实对话框; 无界面场景可注入自动确认)。返回是否确认。</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    // ------------------------------------------------------------- 列表操作

    private void RefreshCards()
    {
        Cards.Clear();
        foreach (var scheme in Config.ActionSchemes)
        {
            Cards.Add(new SchemeCardVm(this, scheme));
        }
        OnPropertyChanged(nameof(HasCards));
    }

    /// <summary>打开方案编辑页 (复刻 openScheme -> router.push)。</summary>
    public void OpenEdit(ActionScheme scheme)
    {
        EditVm = new SelectedActionEditViewModel(_main, this, scheme);
    }

    /// <summary>回到列表 (复刻 goBack)。</summary>
    public void ShowList()
    {
        EditVm = null;
        RefreshCards();
    }

    /// <summary>
    /// 新建方案 (复刻 createNew): POST /api/action-schemes 由后端分配 id 并持久化,
    /// 成功后同步进内存 store 并跳转编辑页; 失败弹「创建失败」提示。
    /// </summary>
    [RelayCommand]
    private async Task CreateNewAsync()
    {
        if (Api is null) return;
        var resp = await Api.CreateActionSchemeAsync(ActionSchemeCatalog.CreateScheme());
        if (!resp.Success || resp.Value is null)
        {
            _main.ShowMessage(I18n.T("974"), I18n.T("973") + (resp.ErrorMessage ?? $"HTTP {resp.StatusCode}"));
            return;
        }
        Config.ActionSchemes.Add(resp.Value);
        OpenEdit(resp.Value);
    }

    /// <summary>删除确认 (复刻 askDelete -> confirmDelete): 从内存移除后经 PUT /config 保存。</summary>
    public async Task AskDeleteAsync(SchemeCardVm card)
    {
        var name = string.IsNullOrEmpty(card.Scheme.Name) ? I18n.T("961") : card.Scheme.Name;
        var confirmed = ConfirmAsync is not null
            ? await ConfirmAsync(I18n.T("968"), string.Format(I18n.T("969"), name))
            : false;
        if (!confirmed) return;

        Config.ActionSchemes.Remove(card.Scheme);
        await SaveConfigAsync();
        RefreshCards();
    }

    /// <summary>主配置保存 (复刻 store.saveConfig; 删除/开关语义为「立即保存」故跳过节流)。</summary>
    public Task<bool> SaveConfigAsync() => _main.SaveAsync(force: true);

    // ------------------------------------------------------------- 语言刷新

    public void OnLanguageChanged()
    {
        LanguageTick++;
        foreach (var card in Cards) card.RefreshLanguage();
        EditVm?.OnLanguageChanged();
    }
}

