/**
 * APIBridge —— 插件 API 视图工厂 (docs/CONTRACTS.md §3.7 暴露面清单)。
 * 阶段 5 落地权限门控骨架: Create(permissions) 返回按权限裁剪的 APIView;
 * 未授权命名空间调用 → 记日志 + 返回空值, 不抛出 (错误隔离, 约束 4)。
 *
 * 权限 → 命名空间映射 (契约 §4 词表 6 项 -> §3.7 命名空间 7 组):
 *   selection -> selection.*          (GetSelectedText / GetSelectedFiles)
 *   clipboard -> selection.*          (文件清单经剪贴板 CF_HDROP, 同族归并)
 *   window    -> window.*
 *   run       -> run.* + send.*       (自动化发送为运行类动作的配套能力)
 *   settings  -> config.*
 *   events    -> events.*
 *   ui.* (Tip / ConfirmBox) 为基础反馈, 不门控, 所有插件可用。
 *
 * 委托实现留待生成端接入真实插件时补全 (阶段 5 用户裁定: 框架先行)。
 */
class APIBridge {
  ; 权限 -> 命名空间列表
  static PermNamespace := Map(
    "selection", ["selection"],
    "clipboard", ["selection"],
    "window", ["window"],
    "run", ["run", "send"],
    "settings", ["config"],
    "events", ["events"]
  )

  /**
   * 给定权限数组, 返回按权限裁剪的 APIView 实例。
   */
  static Create(permissions) {
    return APIView(permissions)
  }
}

class APIView {
  permissions := []
  _granted := Map()              ; namespace -> true

  __New(permissions) {
    this.permissions := permissions
    if (IsObject(permissions)) {
      for p in permissions {
        if (APIBridge.PermNamespace.Has(p)) {
          for ns in APIBridge.PermNamespace[p]
            this._granted[ns] := true
        }
      }
    }
  }

  _has(ns) {
    return this._granted.Has(ns)
  }

  _deny(ns) {
    try {
      FileAppend(FormatTime(, "yyyy-MM-dd HH:mm:ss") " APIBridge denied namespace '" ns "'`n", "logs\plugin_manager.log")
    }
    return ""
  }

  ; ---- selection.* (接入时委托 SelectionContext) ----
  GetSelectedText() {
    if (!this._has("selection"))
      return this._deny("selection")
    return "" ; TODO 接入: SelectionContext.Get()
  }

  GetSelectedFiles() {
    if (!this._has("selection"))
      return this._deny("selection")
    return "" ; TODO 接入: SelectionContext.Get(&isFile), 仅返回文件情形
  }

  ; ---- window.* (接入时委托 Actions.ahk / Functions.ahk) ----
  ; TODO 接入: ActivateWindow / SmartCloseWindow / LoopRelatedWindows /
  ;            GoToLastWindow / MinimizeWindow / MaximizeWindow /
  ;            CenterAndResizeWindow / ToggleWindowTopMost / MoveWindowToNextMonitor

  ; ---- send.* (接入时委托原生 Send 封装) ----
  ; TODO 接入: SendText / SendKeys

  ; ---- run.* (接入时委托 Actions.ahk; ActivateOrRun 默认子进程化) ----
  ; TODO 接入: RunProgram / RunScript / ActivateOrRun

  ; ---- ui.* (基础反馈, 不门控) ----
  Tip(msg) {
    return true ; TODO 接入: Tip(msg)
  }

  ConfirmBox(msg) {
    return false ; TODO 接入: ConfirmBox(msg)
  }

  ; ---- config.* (接入时委托 ConfigProvider, 随首个真实插件落地) ----
  GetSetting(key) {
    if (!this._has("config"))
      return this._deny("config")
    return "" ; TODO 接入: ConfigProvider.GetPluginSetting
  }

  SetSetting(key, value) {
    if (!this._has("config"))
      return this._deny("config")
    return false ; TODO 接入: ConfigProvider.SetPluginSetting
  }

  ; ---- events.* (阶段 6 桥接 IKeyEventBus) ----
  Subscribe(eventType, callback) {
    if (!this._has("events"))
      return this._deny("events")
    return 0 ; TODO 阶段 6: EventBus.Subscribe
  }

  Unsubscribe(subId) {
    if (!this._has("events"))
      return this._deny("events")
    return false ; TODO 阶段 6: EventBus.Unsubscribe
  }
}
