# MyKeymap 架构契约文档(方案 D 定稿)

> 本文档是模块化重构的**唯一权威契约**。所有接口先在此定义并冻结,再迁移实现。
> 状态:**骨架版(阶段 0)** — 接口签名已定,实现细节随阶段推进补充。
> 分支:`dev`(原名 refactor/modularize, 重构完成后更名, 继续承载新功能开发)

---

## 0. 总原则

1. **数据进配置,代码进插件**:`config.json` 只存纯数据(热键→动作ID→参数);
   一切代码载荷(自定义函数、AHK 表达式)迁入插件文件(真 `.ahk`,编译期 Include)。
2. **快路径红线**:按键重映射 / 发送按键 / 鼠标动作只在启动期做原生注册,
   永不进入运行时查表,永不经过事件总线。
3. **同接口双挂载**:内置能力与第三方插件实现同一组契约;内置为静态挂载
   (编译期),第三方为动态挂载(运行时)。

## 1. 约束清单(7 条,不可协商)

| # | 约束 | 验证方式 |
|---|---|---|
| 1 | 零行为变更 | 基线真源 = 部署目录 `MyKeymap-2.0-beta33\data\config.json`(已同步仓库);阶段 1 生成脚本与基线字节级一致(忽略 `#Include` 行);阶段 2+ 用注册计划 Oracle diff + `docs/baseline/BEHAVIOR_CHECKLIST.md` |
| 2 | 接口先行 | 契约先写入本文档并冻结,再写实现 |
| 3 | 依赖倒置 | 上层只依赖 Registry/Bus/Provider 抽象;`fileGroupExts` 等表收敛单源 |
| 4 | 错误隔离 + 阻塞隔离 | L1 插件 try/catch + 日志 + 总线广播;回调 ≤50ms;长任务走 ScriptHost 子进程 |
| 5 | 配置兼容 | `config.json` 加 `schemaVersion`(缺省=1);插件设置独立存 `data/plugin-settings.json`;`%selected%` 永久兼容 |
| 6 | 性能红线 | 见总原则 2 |
| 7 | 数据/代码分离 | 新功能禁止向 `config.json` 写入 AHK 表达式 |

## 2. 目录布局(目标态)

```
bin/lib/
├── core/        IKeyEventBus.ahk / EventBus.ahk / ModeManager.ahk(现 KeymapManager) ——
│                  **总线已落地并接入(阶段 6)**: 进程内同步实现 + 5 处发布点,
│                  零订阅者时行为不变; IKeyEventBus 接口冻结
├── context/     SelectionContext.ahk
├── actions/     ActionRegistry.ahk / IAction.ahk / IRegistration.ahk —— **已完成(阶段 3)**:
│                  三件套按 §3.2-3.4 冻结接口落地(含 ActionContext), 冒烟测试 8 项全过;
│                  仅定义不接入运行路径, 模板与生成产物不变 (快路径仍编译期直连)
│   └── builtins/  9 类内置动作各一文件 —— **已完成(阶段 3)**:`Actions.ahk` 的 42 个函数已按
│                  TypeID 拆入 `builtins/type{1,2,3,4,6,7,8,9}_*.ahk`(函数体逐行搬运,
│                  行多重集校验通过);`Actions.ahk` 保留为聚合 include 入口,模板与生成产物不变
├── rules/       SelectionEngine.ahk(只匹配,不执行)
├── commands/    CommandResolver.ahk / FuzzyStrategy.ahk —— **CommandResolver 已完成(阶段 4)**:
│                  缩写 switch 换为运行时注册表 (闭包即待执行数据), FuzzyStrategy 留桩
├── plugins/     PluginManager.ahk / APIBridge.ahk / ScriptHost.ahk / Plugins.ahk(聚合入口) ——
│                  **框架已落地(阶段 5)**: 权限词表校验/错误隔离/权限裁剪 API 视图/子进程长任务,
│                  冒烟 23 项全过; 仅定义不接入, Everything 插件推迟 (见 §3.7 注记)
└── compat/      LegacyLoader.ahk(消费 Go 编译的遗留代码载荷)—— **占位已建(阶段 3)**:
│                  仅定义不接入; 接入点见文件头注释 (阶段 5 收口 / 阶段 6 事件广播已具备: EventBus)
data/
├── config.json / plugins/<id>/ / plugin-settings.json
config-server/internal/script/generators/   (actionMap 按类型拆分)
```

## 3. 核心接口

### 3.1 IKeyEventBus — 按键/模式事件总线(core/)

```ahk
; ============================================================
; IKeyEventBus —— 按键事件总线抽象(薄观察层)。
; ⚠️ 此接口后续可用 Rust FFI 实现:
;    Rust 守护进程接管键盘捕获与事件分发,通过本地 IPC / FFI 回调
;    向 AHK 层投递事件;AHK 侧订阅方代码无需任何改动即可完成引擎替换。
;    Rust 守护进程同时充当 L2 插件通道宿主(见 3.7)。
; 红线:总线仅承载慢事件,绝不参与快路径(重映射/发键/鼠标)决策链。
; ============================================================
class IKeyEventBus {
  ; eventType 词表(冻结):
  ;   "mode_enter" / "mode_exit"     模式进栈/出栈, eventData: {name}
  ;   "abbr_submit"                  缩写命令提交,  eventData: {source: "caps"|"semi", command, matched, fuzzy: bool}
  ;   "selection_action"             选中动作触发,  eventData: {schemeId, ruleIndex, selected}
  ;   "plugin_loaded" / "plugin_error"  插件生命周期, eventData: {pluginId, message?}
  Subscribe(eventType, callback) => 0   ; 返回订阅 ID
  Unsubscribe(subId) {}
  Publish(eventType, eventData) {}
}
```

**实现约定**:当前阶段 `EventBus` 为进程内同步实现;`Publish` 内对每个订阅
回调独立 `try/catch`,单个回调异常不影响其他订阅者(约束 4)。

**已完成(阶段 6)**:`core/EventBus.ahk` 按冻结接口落地(胖箭头改写为普通方法体),
发布点接入 5 处:① `KeymapManager.Activate` 进/出栈 → `mode_enter`/`mode_exit`;
② `KeymapManager._lock`/`Unlock` 锁定切换 → 同两事件;③ `CommandResolver.Resolve`
提交即报 `abbr_submit`(命中与否都报, `source` 由 scope 映射 "caps"|"semi");
④ `SelectedAction.RunActionScheme` 规则命中后执行前 → `selection_action`;
⑤ `PluginManager` 注册成功/拒绝 → `plugin_loaded`/`plugin_error`(`ActionRegistry`
重复注册同报 `plugin_error`)。所有发布点均 `try` 包裹 + 总线自身静默兜底,
零订阅者时为空遍历, 行为不变。模板新增 2 行 include, 生成脚本仅多此 2 行(已验证),
`/Validate` 全脚本 exit=0, Oracle 回归 PASS, 冒烟 13 项全过(订阅/词表拒绝/
回调隔离/退订/各发布点载荷字段/APIBridge events 桥接与拒绝)。

### 3.2 IAction — 执行型动作契约(actions/)

```ahk
class IAction {
  Type := ""            ; 唯一标识。内置: "activateOrRun"/"systemActions"/...
                        ; 插件: "plugin:<pluginId>:<actionName>"
  CanRunInAbbr := false ; 是否允许出现在缩写命令上下文
  Validate() => ""      ; 返回错误信息, 空串 = 合法(注册/插件加载时调用)
  Execute(ctx) {}       ; ctx: ActionContext
  Preview(ctx) => ""    ; 执行预览(设置页模拟测试, 对齐现 PreviewAction 语义)
}

; ActionContext — 动作执行上下文(只读数据袋)
;   .selected   : String  选中文本/文件路径(多文件换行分隔), 未获取为 ""
;   .isFile     : Bool    selected 是否为文件(CF_HDROP)
;   .winTitle   : String  触发时的窗口条件
;   .params     : Map     动作参数(来自配置数据)
;   .source     : String  "hotkey" | "abbr" | "selection" | "plugin"
```

### 3.3 IRegistration — 声明型契约(覆盖快路径)

```ahk
class IRegistration {
  ; 向指定 Keymap 声明绑定。重映射必须声明为原生注册(由 AHK 钩子直接
  ; 处理),不得包装为回调;模式结构(子模式/锁定/单击动作)同理。
  Declare(km) {}
  ; 声明内容类别(冻结): "hotkey-action" | "remap" | "submode" | "singlePress"
}
```

### 3.4 ActionRegistry — 动作注册表

```ahk
class ActionRegistry {
  static Actions := Map()              ; Type -> IAction
  static Register(action) {}           ; 重复 Type: 记日志 + plugin_error, 不覆盖先到者
  static Unregister(type) {}           ; 插件卸载时调用
  static Get(type) => actionOrBlank
  static Execute(type, ctx) {}         ; 统一 try/catch → 日志 + Tip; 快路径动作禁止走此入口
}
```

**已完成(阶段 3)**:§3.2 `IAction`+`ActionContext`、§3.3 `IRegistration`、§3.4 `ActionRegistry`
已按冻结接口落地于 `bin/lib/actions/`(胖箭头方法改写为普通方法体, 因部署版 AHK 不支持;
接口形状不变)。冒烟测试覆盖: 注册/重复拒绝(先到者胜)/空 Type 拒绝/未知 Get 返回空串/
Execute 传 ctx/异常隔离不抛出/未知 Type 不抛出/内置类型拒绝注销——全部通过。
`compat/LegacyLoader.ahk` 占位已建。四者均未被模板或生成脚本引用, 零行为变更。

### 3.5 SelectionContext — 选中文本统一入口(context/)

```ahk
class SelectionContext {
  static Get(&isFile := false) => ""   ; 合并现 GetSelectedText + GetSelectedContent
                                       ; 能力: ^c 获取 + CF_HDROP 文件判断 + 剪贴板恢复
  static Normalize(template, selected) => ""
                                       ; 占位符归一: {selected} 为规范形;
                                       ; %selected% 解析时自动等价转换(永久兼容, 文档标废弃)
}
```

### 3.6 CommandResolver — 缩写命令解析(commands/)

```ahk
class CommandResolver {
  static Table := Map()                ; "scope:command" -> [CommandStep, ...] 运行时表, 取代生成 switch;
                                       ; scope 分域 ("capslock" | "semicolon"):
                                       ; 两表命令名可重复, 必须隔离 (阶段 4 修订)
  static Strategy := ""                ; 解析策略对象, 可插拔 (阶段 4 留桩; 空串 = 未设置,
                                       ; 部署版 AHK v2.0.19 无 null 关键字)
  static Register(scope, command, steps) {}
                                       ; 由生成脚本在 InitKeymap 内调用 (路径变量之后);
                                       ; 重复命令: 记日志, 不覆盖先到者 (约束 4)
  static Resolve(scope, command, hook := "") {}
  ; 策略契约(冻结): 精确命中 → 按 steps 顺序执行;
  ;   带窗口组守卫的 step: 守卫命中 → 执行并立即返回 (对齐旧 switch 语义);
  ;   未命中 → 子序列匹配 → 编辑距离≤1 → 候选集;
  ;   唯一候选 → 静默执行 + Tip 提示实际命令; 多候选 → 仅 Tip 列出, 不执行。
  ; 当前阶段(阶段 4)只实现精确匹配, 未命中静默无操作, FuzzyStrategy 留桩。
}

; 步骤数据: 闭包即"待执行数据"。生成端把原 switch case 体编译为闭包,
; 保留任意表达式 (含遗留代码载荷/路径变量引用), 参数在执行时求值,
; {selected} 等运行时替换语义不变。
class CommandStep {
  call := ""                           ; Funcref, 无参闭包 () => <原 case 体语句>
  winTitle := ""                       ; 窗口组守卫 (仅 WindowGroupID != 0 的动作有)
  conditionType := 0                   ; 0 = 无守卫; 1-5 语义同 matchWinTitleCondition
}
```

**已完成(阶段 4)**:`bin/lib/commands/CommandResolver.ahk` 实现上述契约(含 `DumpAbbr(outFile)`
Oracle 导出: 命令按字典序, 同配置多次调用结果一致);生成端 `generators/abbr_registry.go`
的 `AbbrRegistryCode` 与 `AbbrToCode` 逐条对齐, 模板中 `ExecCapslockAbbr`/`ExecSemicolonAbbr`
改为委托 `CommandResolver.Resolve`。验证: 新旧产物 diff 审查(35 case → 35 闭包逐字一致) +
语义冒烟 7 项 + 整脚本 `/Validate` + **Oracle diff PASS**(capslock 命令集 35/35 与步骤数相等,
分号段双空)。
阶段 4 实测坑两条(影响任何独立测试脚本): ① AHK v2 的 `>` 对两个字符串按**数字**比较
(非数字串直接报错), 字典序排序必须用 `StrCompare`; ② `type9_mykeymap.ahk` 调用仅存在于
生成脚本的 `ExecCapslockAbbr`, AHK v2 默认 #Warn 在**加载期**弹警告对话框阻塞进程,
`/ErrorStdOut` 无法抑制——独立测试脚本头部须加 `#Warn All, Off`。

### 3.7 PluginManager / APIBridge / ScriptHost(plugins/)

```ahk
class PluginManager {
  static Plugins := Map()              ; pluginId -> {manifest, actions, enabled}
  static ScanAndLoad(dir) {}           ; 扫描→清单校验→权限检查→加载;
                                       ; 单插件失败: 日志 + Publish("plugin_error"), 不影响其他
  static Unload(id) {}
  static GetAPI(id) => api             ; 按 manifest.permissions 裁剪的 APIBridge 视图
}

class ScriptHost {
  static Run(scriptPath, args := "", inBackground := true) ; 子进程执行长任务
                                       ; (承接现 RunScriptWithSelected 的执行方式)
}
```

**APIBridge 暴露面清单**(L1 直接调用;L2 映射为同名 JSON-RPC method,
协议已冻结(阶段 6),宿主 = 未来 Rust 守护进程,当前不落地):

**L2 协议文本(冻结)**:
- 传输:子进程 **stdio**, 一行一条消息(`\n` 分隔, UTF-8, 无长度前缀);
- 格式:JSON-RPC 2.0;宿主 → 守护进程 = request(带 `id`),守护进程 → 宿主 = response;
- method 命名:与 APIBridge 命名空间同名(`selection.GetSelectedText` 等);
- 事件推送:守护进程 → 宿主用 notification `events.notify`,
  `params: {type: eventType, data: eventData}`(eventType 词表见 §3.1);
- 错误码:保留 JSON-RPC 标准段(-32700/-32600/-32601/-32602/-32603);
  `-32001` = 权限拒绝(对应 APIBridge 未授权命名空间);
- 鉴权:无(本机单用户进程间通道);超时:调用方自定,宿主不设默认。

| 命名空间 | 方法 | 来源 |
|---|---|---|
| `selection.*` | GetSelectedText / GetSelectedFiles | SelectionContext |
| `window.*` | ActivateWindow / SmartCloseWindow / LoopRelatedWindows / GoToLastWindow / MinimizeWindow / MaximizeWindow / CenterAndResizeWindow / ToggleWindowTopMost / MoveWindowToNextMonitor | Actions.ahk / Functions.ahk |
| `send.*` | SendText / SendKeys | 原生 Send 封装 |
| `run.*` | RunProgram / RunScript / ActivateOrRun(**默认子进程化**) | Actions.ahk |
| `ui.*` | Tip / ConfirmBox | Utils.ahk |
| `events.*` | Subscribe / Unsubscribe | 桥接 IKeyEventBus |
| `config.*` | GetSetting / SetSetting(读写 plugin-settings.json) | ConfigProvider |

**已完成(阶段 5)**:`bin/lib/plugins/` 落地 `PluginManager` / `APIBridge` / `ScriptHost`
三件套框架(含 permissions 词表校验、错误隔离、按权限裁剪的 API 视图、子进程长任务),
冒烟测试覆盖注册/重复拒绝/权限非法拒绝/未知 Get/按权限裁剪/未授权调用不抛出/卸载。
**范围调整(用户裁定)**:① AHK v2 无运行时动态加载能力, L1 插件 `main.ahk` 须经编译期
`#Include`——生成端接入(扫描 `data/plugins` 生成 Include 行 + 调用 `Register(api)`)留待后续;
② 首个官方插件 `everything-search`(含 `everythingPath` 设置项与自动启动逻辑)**推迟到全部阶段完成后**再做;
③ `ConfigProvider` 的 JSON 解析(`plugin-settings.json` 读写)随首个真实插件落地。
本阶段仅定义不接入运行路径, 模板与生成产物不变(零行为变更)。

### 3.8 ConfigProvider — 配置读取收口

```ahk
class ConfigProvider {
  static Load(path) => configMap       ; JSON 解析(AHK 无内置 JSON 库, 用随附 JSON.ahk)
  static SchemaVersion(cfg) => 1       ; 缺省视为 1; 迁移钩子按版本号链式执行
  static GetPluginSetting(pluginId, key) / SetPluginSetting(...)
                                       ; 独立存储于 data/plugin-settings.json
}
```

## 4. 插件清单格式(冻结)

`data/plugins/<id>/plugin.json`:

```json
{
  "id": "everything-search",
  "name": "Everything 本地搜索",
  "version": "1.0.0",
  "runtime": "ahk",
  "entry": "main.ahk",
  "provides": { "actions": ["everythingSearch"], "abbrCommands": ["fs"] },
  "permissions": ["selection", "run", "settings"],
  "settings": {
    "everythingPath": { "type": "path", "label": "Everything.exe 路径" }
  }
}
```

- `runtime`:`"ahk"` = L1 进程内(当前支持);`"process"` = L2(预留,清单可声明,加载器报"暂不支持")
- `permissions` 词表(冻结):`selection` / `run` / `clipboard` / `window` / `settings` / `events`
- L1 插件入口约定:`main.ahk` 必须导出 `Register(api)` 函数,由 PluginManager 调用

## 5. Go 生成端契约

- 阶段 3 起,`config-server/internal/script/action.go` 的 `actionMap` 拆入
  `generators/` 目录,每个 TypeID 一文件;
  **已完成(阶段 3)**:数据模型在 `model/` 包,9 个 TypeID 渲染函数各一文件,
  原文件已删除,外部调用经 `script` 包类型别名兼容,生成产物逐字节回归通过;
- 生成器新增义务:可输出 **注册计划 JSON**(`settings.exe DumpPlan <config> <out>`),
  与 AHK 运行时加载器的注册计划 diff(Oracle 机制);
  **Go 侧已实现(阶段 3)**:`generators/plan.go` 的 `BuildPlan` 与渲染路径同源推导,
  输出确定性已验证(两次运行逐字节一致);
  **AHK 侧已对接(阶段 4)**:`CommandResolver.DumpAbbr` 导出运行时实际注册表,
  与 DumpPlan 的 abbr 段对比命令集 + 步骤数, 首次运行即 PASS(动作参数级对比留待阶段 5);
- ~~`preprocess()` 的 `!f17` 注入逻辑缺陷~~(已证伪):`Preprocess` 遍历逻辑本身正确;
  真实缺陷是 `GenerateAHK` 验证命令不调用 `Preprocess`(仅运行时 `GenerateScripts` 调用),
  导致验证产物缺 `!f17`,此前"`!f17` 为手工行"的认知作废。
  阶段 3 已修复:`Preprocess` 导出,`GenerateAHK` 对齐运行时路径,
  修复后生成产物与部署运行产物**逐行完全一致**(已验证);
- 遗留代码载荷(`ahkCode` / `ahk-expression:` / `ahk:` 行 / conditionType 5 表达式)
  继续由 Go 编译,输出到 `compat/` 消费格式,直至用户迁移为插件。

## 6. 未来功能落点(不在本次实现)

| 功能 | 落点 |
|---|---|
| 1. 命令模糊输入 | **已实现(2026-08-22)**: 逐字符实时后缀校验 `FuzzySuffixFire`(接在 `InputHook.OnChar`,最长后缀优先);命中即停钩执行,`abbr_submit.fuzzy=true`。`Strategy` 桩保留给未来的编辑距离≤1/候选提示类策略 |
| 2. Everything 搜索 | 首个官方插件 `everything-search`(含 `everythingPath` 设置项与自动启动逻辑)——**阶段 5 裁定推迟到全部阶段完成后**;框架已就位(§3.7) |
| 3. 外接脚本/函数 | L1 插件 = `custom_functions.ahk` 正式化;长任务走 ScriptHost |
| 4. 开放 API/插件市场 | 双层插件体系 + 权限化 APIBridge;内置同接口倒逼 API 完备 |
| 5. Rust 重写 | IKeyEventBus 抽象已就位;Rust 守护进程 = 总线实现 + L2 宿主 |

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-08-22 | 骨架版建立(方案 D 定稿),全部接口签名冻结 |
| 2026-08-22 | 阶段 3/4/5 完成注记; 阶段 5 裁定 Everything 插件推迟 |
| 2026-08-22 | 阶段 6: EventBus 落地并接入 5 处发布点; L2 JSON-RPC 协议文本冻结; 模板补记阶段 4 格式 |
| 2026-08-22 | §6 第 1 条落地: 命令模糊输入(逐字符后缀校验, 最长优先, CapsLock 域); `Resolve` 增 `fuzzy` 可选参数(默认不变); `config-server/templates` 副本补齐阶段 6 include |
| 2026-08-23 | 修复存量缺陷: `ActivateWindow` 谓词排除桌面壳窗口 (Progman) —— 此前 `ahk_exe explorer.exe` 类匹配把常驻桌面窗口当成"已打开的窗口"提前返回, 导致 fe 等命令在无真实窗口时永远不启动目标程序 (冷启动失效); 实证与模糊输入功能无关 |
| 2026-08-23 | 死代码/过时代码审计与清理 (零行为变更): ① 删 `Functions.ahk` 孤立工具 `MapFindKey`/`Join` (零调用); ② 删 Go 端 `AbbrToCode` 生成器及其 FuncMap 注册 (阶段 4 已被 `AbbrRegistryCode` 取代, 模板零引用) + `command.go` panic 后不可达 return (go vet); ③ 删 config-ui 脚手架残留 `HelloWorld.vue` (零 import); ④ 部署目录 `bin/lib/` 平铺旧库 7 文件 (重构前布局残留, 仅基线文档引用) 备份后删除, 同步 `ActionRegistry.ahk` 部署副本至 phase6 版 (留桩, 不在运行链) |
| 2026-08-23 | 设置入口启动延迟优化 (零行为变更): ① `openBrowser` 删除端口就绪后固定 600ms 延迟 (net.Listen 成功后端口已可接受连接, 实测 Go 业务初始化仅 ~20ms); ② `MyKeymapOpenSettings.launchSettings` 改为 wt 直接运行 `settings.exe` (绝对路径) 绕过 pwsh -NoExit 层 (实测启动链热 wt 660ms→~200ms, 窗口行为不变); 实测基线: 热场景全链 ~1465ms→~405ms, 冷场景 ~2480ms→~1150ms; GenerateScripts 产物逐字节一致 |
