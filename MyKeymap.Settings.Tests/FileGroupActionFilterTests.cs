using MyKeymap.Settings.Models;
using MyKeymap.Settings.Services;
using MyKeymap.Settings.ViewModels;

namespace MyKeymap.Settings.Tests;

/// <summary>
/// 「文件后缀 (fileExt)」语境行为下拉按分组过滤 + 分组后缀修改保存写回 的单元测试。
/// 纯 ViewModel 测试 (无 Avalonia 宿主): RuleEditorVm 为 ObservableObject, 构造链
/// MainViewModel -> SelectedActionPageViewModel -> SelectedActionEditViewModel 均为
/// 内存对象 (BackendSession 构造只存参不拉起子进程, Config 直接注入, ISettingsApi 不被触达)。
/// 行为选项自 2026-09 起由行为包 appliesTo 覆盖推导 (BehaviorCatalog, CONTRACTS §3.9):
/// 文件语境覆盖集 = 通配内置行为 6 项 (专属排前), default 标记推导纠正默认行为。
/// </summary>
public sealed class FileGroupActionFilterTests
{
    /// <summary>与 data/config.json 默认分组一致的夹具 (image/code 两组足够覆盖过滤与写回)。</summary>
    private static readonly string[] ImageExts = ["jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico"];
    private static readonly string[] CodeExts = ["c", "cpp", "h", "py", "go", "rs", "json"];

    /// <summary>行为目录种子 (CONTRACTS §3.9): 内置 11 包由共享夹具构造, 一次性注入。</summary>
    private static void EnsureBehaviorCatalog()
    {
        if (BehaviorCatalog.Loaded) return;
        BehaviorCatalog.SeedForTests(BehaviorFixtures.Builtin(), []);
    }

    private static Config BuildConfig() => new()
    {
        FileGroups =
        [
            new FileGroup { Name = "image", Label = "图片", Exts = [.. ImageExts] },
            new FileGroup { Name = "code", Label = "代码", Exts = [.. CodeExts] },
        ],
    };

    /// <summary>
    /// 构造编辑页 VM (单条 fileExt 规则: MatchValue=matchValue, ActionType=actionType)。
    /// 构造完成即选中该规则并自动创建规则编辑器 (OnSelectedRuleChanged -> RuleEditorVm -> ApplyFromRule)。
    /// </summary>
    private static (SelectedActionEditViewModel Vm, Config Config) CreateHost(string matchValue, string actionType)
        => CreateHost(BuildConfig(), matchValue, actionType);

    /// <summary>重载: 注入自定义 Config (二义同集分组 / 带点存量分组等场景)。</summary>
    private static (SelectedActionEditViewModel Vm, Config Config) CreateHost(Config config, string matchValue, string actionType)
    {
        EnsureBehaviorCatalog();
        var main = new MainViewModel(new BackendSessionOptions());
        main.Config = config;
        var page = new SelectedActionPageViewModel(main);
        var scheme = new ActionScheme
        {
            Name = "测试方案",
            Hotkey = "*!t",
            Enable = true,
            Rules =
            [
                new ActionRule
                {
                    Priority = 1,
                    MatchType = "fileExt",
                    MatchValue = matchValue,
                    ActionType = actionType,
                    ActionValue = "",
                },
            ],
        };
        return (new SelectedActionEditViewModel(main, page, scheme), config);
    }

    /// <summary>行为下拉候选值集合 (无序比较用)。</summary>
    private static HashSet<string> OptionValues(SelectedActionEditViewModel vm)
        => vm.Editor!.ActionTypeOptions.Select(o => o.Value).ToHashSet();

    // ------------------------------------------------------------- ActionTypeOptions 过滤

    /// <summary>关联 image: 文件语境覆盖集 = 通配内置行为 6 项 (专属无), 按目录序排列;
    /// 文本专用行为 (magnet_download/open_url/open_registry/send_keys/search) 不出现。</summary>
    [Fact]
    public void ActionTypeOptions_With_Image_Association_Shows_Covering_Generic_Set()
    {
        var (vm, _) = CreateHost(string.Join(", ", ImageExts), "open");
        Assert.Equal(new[] { "copy", "open", "open_folder", "open_path", "run", "script" },
            vm.Editor!.ActionTypeOptions.Select(o => o.Value));
    }

    /// <summary>关联 code: 覆盖集与 image 相同 (通配行为对任意后缀恒适用, 2026-09 语义)。</summary>
    [Fact]
    public void ActionTypeOptions_With_Code_Association_Shows_Same_Covering_Set()
    {
        var (vm, _) = CreateHost(string.Join(", ", CodeExts), "script");
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" },
            OptionValues(vm));
    }

    /// <summary>无关联 (手输后缀): 覆盖集同为通配行为 6 项。</summary>
    [Fact]
    public void ActionTypeOptions_Without_Association_Shows_Covering_Generic_Set()
    {
        var (vm, _) = CreateHost("abc,xyz", "run");
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" },
            OptionValues(vm));
    }

    /// <summary>
    /// 存量脏值 (关联 image 但行为是 magnet_download): union 保证脏值恒可见可选,
    /// 其余仍按分组过滤 (send_keys/open_registry 等文本专用行为不可见)。
    /// </summary>
    [Fact]
    public void ActionTypeOptions_Keeps_Dirty_Action_Visible_In_Associated_Group()
    {
        var (vm, _) = CreateHost(string.Join(", ", ImageExts), "magnet_download");
        var values = OptionValues(vm);
        Assert.Contains("magnet_download", values);
        Assert.DoesNotContain("send_keys", values);
        Assert.DoesNotContain("open_registry", values);
        Assert.DoesNotContain("search", values);
    }

    // ------------------------------------------------------------- 分组选择联动

    /// <summary>
    /// 当前行为 magnet_download (仅覆盖文本特征) 选中 image: 纠正为默认行为 open_path
    /// (default 标记推导), 清空残留载荷, 填充分组后缀串, 行为下拉联动刷新。
    /// (2026-09 语义: run 等通配行为对文件前提恒适用, 不再触发纠正。)
    /// </summary>
    [Fact]
    public void OnFileGroupSelected_Corrects_Incompatible_Action_And_Clears_Template()
    {
        var (vm, _) = CreateHost("", "magnet_download");
        var rule = vm.SelectedRule!.Rule;
        rule.ActionValue = "magnet:?xt=1"; // 残留载荷 (magnet_download 本无模板, 验证纠正时清空)
        var editor = vm.Editor!;
        editor.FileGroupSelected = editor.FileGroupOptions.First(o => o.Value == "image");

        Assert.Equal("open_path", rule.ActionType); // 行为包 default 标记推导
        Assert.Equal("", rule.ActionValue);         // 纠正到无参行为时清空残留载荷
        Assert.Equal(string.Join(", ", ImageExts), rule.MatchValue);
        // 联动刷新: 选项为文件语境覆盖集
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm));
    }

    /// <summary>当前行为 open (分组安全集内): 选中 image 不纠正行为, 仅填充后缀串。</summary>
    [Fact]
    public void OnFileGroupSelected_Keeps_Compatible_Action_Untouched()
    {
        var (vm, _) = CreateHost("", "open");
        var rule = vm.SelectedRule!.Rule;
        var editor = vm.Editor!;
        editor.FileGroupSelected = editor.FileGroupOptions.First(o => o.Value == "image");

        Assert.Equal("open", rule.ActionType);
        Assert.Equal(string.Join(", ", ImageExts), rule.MatchValue);
    }

    /// <summary>选中「无」: 清空条件值并解除关联 (行为过滤回退全文件集)。</summary>
    [Fact]
    public void OnFileGroupSelected_None_Clears_MatchValue_And_Disassociates()
    {
        var (vm, _) = CreateHost(string.Join(", ", ImageExts), "open");
        var editor = vm.Editor!;
        editor.FileGroupSelected = editor.FileGroupOptions.First(o => o.Value == ""); // 「无」

        Assert.Equal("", vm.SelectedRule!.Rule.MatchValue);
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm));
    }

    // ------------------------------------------------------------- 回填重建关联

    /// <summary>条件值 = 分组后缀全集的乱序/大小写变体: 回填时重建关联并选中该分组。</summary>
    [Fact]
    public void ApplyFromRule_Rebuilds_Association_From_Shuffled_Case_Variant()
    {
        var (vm, _) = CreateHost("WEBP, svg, ICO, jpg, JPEG, Png, gif, BMP", "open");
        var editor = vm.Editor!;
        Assert.Equal("image", editor.FileGroupSelected?.Value);
        // 乱序大小写变体仍命中 image 词表 -> 选项按 image 过滤
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm));
    }

    /// <summary>自定义后缀串 (组内子集, 不与任何分组全集一致): 不关联, 快捷填入保持未选。</summary>
    [Fact]
    public void ApplyFromRule_Does_Not_Associate_Custom_Ext_List()
    {
        var (vm, _) = CreateHost("jpg, png", "open"); // image 组的子集 != 全集
        Assert.Null(vm.Editor!.FileGroupSelected);
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm));
    }

    // ------------------------------------------------------------- 保存写回

    /// <summary>关联 image 后手改条件值: 写回更新 Config.FileGroups[image].Exts (以编辑框当前顺序)。</summary>
    [Fact]
    public void ApplyFileGroupWriteBack_Saves_Edited_ExtList_To_Group()
    {
        var (vm, config) = CreateHost(string.Join(", ", ImageExts), "open");
        vm.Editor!.MatchValue = "jpg, png, webp";
        vm.ApplyFileGroupWriteBack();

        var group = config.FileGroups.First(g => g.Name == "image");
        Assert.Equal(new[] { "jpg", "png", "webp" }, group.Exts);
        // 其他分组不受影响
        Assert.Equal(CodeExts, config.FileGroups.First(g => g.Name == "code").Exts);
    }

    /// <summary>条件值清空: 不写回 (组定义保持原样)。</summary>
    [Fact]
    public void ApplyFileGroupWriteBack_Empty_MatchValue_Does_Not_Touch_Group()
    {
        var (vm, config) = CreateHost(string.Join(", ", ImageExts), "open");
        vm.Editor!.MatchValue = "";
        vm.ApplyFileGroupWriteBack();

        Assert.Equal(ImageExts, config.FileGroups.First(g => g.Name == "image").Exts);
    }

    /// <summary>无关联 (手输后缀): 不写回任何分组。</summary>
    [Fact]
    public void ApplyFileGroupWriteBack_Without_Association_Does_Not_Touch_Groups()
    {
        var (vm, config) = CreateHost("custom1, custom2", "open");
        vm.ApplyFileGroupWriteBack();

        Assert.Equal(ImageExts, config.FileGroups.First(g => g.Name == "image").Exts);
        Assert.Equal(CodeExts, config.FileGroups.First(g => g.Name == "code").Exts);
    }

    /// <summary>条件值与分组内容仅大小写/顺序差异: 视为一致, 不写回。</summary>
    [Fact]
    public void ApplyFileGroupWriteBack_Same_Set_Different_Order_Case_Does_Not_Touch_Group()
    {
        var (vm, config) = CreateHost(string.Join(", ", ImageExts), "open");
        vm.Editor!.MatchValue = "WEBP, SVG, ICO, JPG, JPEG, PNG, GIF, BMP"; // 同集合, 乱序+大小写
        vm.ApplyFileGroupWriteBack();

        Assert.Equal(ImageExts, config.FileGroups.First(g => g.Name == "image").Exts);
    }

    // ------------------------------------------------------------- NormalizeExts

    /// <summary>混合分隔符/前导点/空 token/重复 (忽略大小写保留首个书写形式)。</summary>
    [Fact]
    public void NormalizeExts_Handles_Separators_Points_Empty_And_Dupes()
    {
        Assert.Equal(new[] { "JPG", "png", "gif", "webp" },
            ActionSchemeCatalog.NormalizeExts("JPG, .png,, gif; webp"));
        Assert.Equal(new[] { "jpg" }, ActionSchemeCatalog.NormalizeExts("jpg、JPG"));   // 去重保留首个
        Assert.Equal(new[] { "a", "b" }, ActionSchemeCatalog.NormalizeExts("，a;；b、")); // 全角分隔符
        Assert.Empty(ActionSchemeCatalog.NormalizeExts(""));
        Assert.Empty(ActionSchemeCatalog.NormalizeExts(null));
        Assert.Empty(ActionSchemeCatalog.NormalizeExts("  , ., ; "));
    }

    // ------------------------------------------------------------- F2 关联生命周期

    /// <summary>
    /// F5-1: × 解除关联后, 行为类型变更 (间接触发 ApplyFromRule) 不再按值重建;
    /// 经写回公有行为断言: 改条件值后分组内容不变 —— 解除意图不被回滚。
    /// </summary>
    [Fact]
    public void Dissociate_By_Clear_Survives_ActionType_Change_And_WriteBack()
    {
        var (vm, config) = CreateHost(string.Join(", ", ImageExts), "open");
        var editor = vm.Editor!;
        editor.FileGroupSelected = null; // × 解除

        editor.SelectedActionType = editor.ActionTypeOptions.First(o => o.Value == "copy"); // 触发一次回填

        editor.MatchValue = "jpg, png, bmp"; // 手改条件值
        vm.ApplyFileGroupWriteBack();
        Assert.Equal(ImageExts, config.FileGroups.First(g => g.Name == "image").Exts);
        Assert.DoesNotContain(vm.SelectedRule!.Rule, vm.PendingGroupAssociations.Keys);
    }

    /// <summary>
    /// F5-2 (1007 承诺): 选分组 → 手改后缀 → 换行为类型 → 写回仍更新分组
    /// (关联不因「值不再≡分组」被解除)。
    /// </summary>
    [Fact]
    public void Edited_ExtList_Survives_ActionType_Change_WriteBack()
    {
        var (vm, config) = CreateHost("", "open");
        var editor = vm.Editor!;
        editor.FileGroupSelected = editor.FileGroupOptions.First(o => o.Value == "image"); // 建立关联
        editor.MatchValue = "jpg, png"; // 手改后缀 (不再≡分组全集)
        editor.SelectedActionType = editor.ActionTypeOptions.First(o => o.Value == "copy"); // 换行为类型

        vm.ApplyFileGroupWriteBack();
        Assert.Equal(new[] { "jpg", "png" }, config.FileGroups.First(g => g.Name == "image").Exts);
    }

    /// <summary>
    /// F5-3: 显式关联 + 手改后缀 → 编辑器跨规则重建 → 关联经页面级映射恢复 → 再写回仍生效。
    /// </summary>
    [Fact]
    public void Association_Restored_From_Page_Map_After_Editor_Rebuild()
    {
        var (vm, config) = CreateHost("", "open");
        var editor = vm.Editor!;
        editor.FileGroupSelected = editor.FileGroupOptions.First(o => o.Value == "image");
        editor.MatchValue = "jpg, webp"; // 手改后缀

        vm.Editor = new RuleEditorVm(vm, vm.SelectedRule!.Rule); // 模拟切走再切回的顶层重建

        // 关联经映射恢复: 行为下拉按 image 过滤
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm));
        vm.Editor!.MatchValue = "jpg, gif";
        vm.ApplyFileGroupWriteBack();
        Assert.Equal(new[] { "jpg", "gif" }, config.FileGroups.First(g => g.Name == "image").Exts);
    }

    /// <summary>
    /// F5-4: fileExt 关联 → 切 textType: 关联解除 (映射移除)、条件值重置为 url;
    /// 切回 fileExt 后行为集回退 FileActions 全文件集且写回不生效。
    /// </summary>
    [Fact]
    public void Switch_MatchType_Away_From_FileExt_Dissociates_And_Blocks_WriteBack()
    {
        var (vm, config) = CreateHost(string.Join(", ", ImageExts), "open");
        var editor = vm.Editor!;
        editor.SelectedMatchType = editor.MatchTypeOptions.First(o => o.Value == "textType");

        Assert.Equal("url", vm.SelectedRule!.Rule.MatchValue); // textType 默认特征
        // url 词表 (textType 语境无分组过滤, image 词表不再生效; textType 组合不 union 展示,
        // 由后端校验与文本特征联动纠正), image 专属的 open_path/open_folder/copy 均不可见
        Assert.Equal(new HashSet<string> { "open_url", "search" }, OptionValues(vm));
        Assert.DoesNotContain(vm.SelectedRule.Rule, vm.PendingGroupAssociations.Keys);

        editor.SelectedMatchType = editor.MatchTypeOptions.First(o => o.Value == "fileExt"); // 切回
        Assert.Equal(new HashSet<string> { "copy", "open", "open_folder", "open_path", "run", "script" }, OptionValues(vm)); // 回退文件语境覆盖集
        vm.ApplyFileGroupWriteBack();
        Assert.Equal(ImageExts, config.FileGroups.First(g => g.Name == "image").Exts); // 写回不生效
    }

    // ------------------------------------------------------------- F4 归一化

    /// <summary>F4: NormalizeExts 两端去点 (含尾点+尾随空格); SameExts 双侧归一化。</summary>
    [Fact]
    public void NormalizeExts_Trims_Both_Ends_And_SameExts_Normalizes_Both_Sides()
    {
        Assert.Equal(new[] { "jpg", "png" }, ActionSchemeCatalog.NormalizeExts(".jpg, png."));
        Assert.Equal(new[] { "jpg" }, ActionSchemeCatalog.NormalizeExts("jpg. ")); // 尾点死条目
        // 双侧归一化: 带点书写与归一化值视为一致
        Assert.True(ActionSchemeCatalog.SameExts(new[] { "jpg", "png" }, new[] { ".jpg", "png." }));
        Assert.True(ActionSchemeCatalog.SameExts(new[] { ".jpg." }, new[] { "jpg" }));
        Assert.False(ActionSchemeCatalog.SameExts(new[] { "jpg" }, new[] { "png" }));
    }

    /// <summary>
    /// F4 动机: 带点存量分组与归一化条件值视为一致 —— 建立关联且写回不重写 (保留存量书写形式)。
    /// </summary>
    [Fact]
    public void WriteBack_Does_Not_Rewrite_Dotted_Group_When_Normalized_Equal()
    {
        var config = BuildConfig();
        config.FileGroups[0].Exts = [".jpg", ".png"]; // 存量带点书写
        var (vm, _) = CreateHost(config, "jpg, png", "open");

        // 归一化后一致 -> 建立关联, 行为下拉按 image 分组过滤
        Assert.Equal("image", vm.Editor!.FileGroupSelected?.Value);
        vm.ApplyFileGroupWriteBack(); // 同集 (归一化意义下) -> 不重写
        Assert.Equal(new[] { ".jpg", ".png" }, config.FileGroups.First(g => g.Name == "image").Exts);
    }

    // ------------------------------------------------------------- F3 二义

    /// <summary>
    /// F5-6: 两组同后缀集二义 —— 无显式关联时按值推导取声明序首个 (FirstOrDefault);
    /// 显式关联后者后, 编辑器重建经页面级映射恢复后者 (映射优先级压过二义), 写回指向后者。
    /// </summary>
    [Fact]
    public void Ambiguous_Same_Ext_Groups_Map_Priority_Keeps_Explicit_Association()
    {
        var config = new Config
        {
            FileGroups =
            [
                new FileGroup { Name = "photo", Label = "照片", Exts = ["jpg", "png"] },
                new FileGroup { Name = "image2", Label = "图片二", Exts = ["jpg", "png"] },
            ],
        };
        var (vm, cfg) = CreateHost(config, "jpg, png", "open");

        // 无显式关联: 首次按值推导二义 -> FirstOrDefault 取声明序首个
        Assert.Equal("photo", vm.Editor!.FileGroupSelected?.Value);

        // 显式关联后者
        vm.Editor.FileGroupSelected = vm.Editor.FileGroupOptions.First(o => o.Value == "image2");
        Assert.Equal("image2", vm.Editor.FileGroupSelected?.Value);

        // 编辑器重建 (切走再切回同路径): 映射恢复 image2, 不被二义推导拉回 photo
        vm.Editor = new RuleEditorVm(vm, vm.SelectedRule!.Rule);
        Assert.Equal("image2", vm.Editor!.FileGroupSelected?.Value);

        // 行为断言: 写回指向 image2, photo 不受影响
        vm.Editor.MatchValue = "jpg, png, bmp";
        vm.ApplyFileGroupWriteBack();
        Assert.Equal(new[] { "jpg", "png", "bmp" }, cfg.FileGroups.First(g => g.Name == "image2").Exts);
        Assert.Equal(new[] { "jpg", "png" }, cfg.FileGroups.First(g => g.Name == "photo").Exts);
    }
}
