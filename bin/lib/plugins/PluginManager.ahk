/**
 * PluginManager —— L1 插件生命周期管理 (docs/CONTRACTS.md §3.7)。
 * 模块化重构阶段 5 落地框架: 元数据管理 / permissions 词表校验 / 错误隔离 / 卸载。
 *
 * 范围说明 (阶段 5 用户裁定):
 *  - AHK v2 无运行时动态加载代码能力, L1 插件 main.ahk 须经编译期 #Include;
 *    生成端接入 (扫描 data/plugins 生成 Include 行 + 调用 Register(api)) 留待后续。
 *  - 首个官方插件 everything-search 推迟到全部阶段完成后。
 *  - 本阶段仅定义不接入运行路径, 模板与生成产物不变 (零行为变更)。
 */
class PluginManager {
  static Plugins := Map()        ; pluginId -> Map{manifest, enabled}
  static Errors := []            ; 加载期错误 [{pluginId, message}], 供调试 / Oracle
  ; permissions 词表 (冻结, 契约 §4)
  static PERMISSIONS := ["selection", "run", "clipboard", "window", "settings", "events"]

  /**
   * 注册插件。manifest 为已解析的 Map (阶段 5 不引入 JSON 库, 文件解析留待接入)。
   * 必需字段: id, entry。permissions 须在词表内。
   * 重复 id: 记日志, 不覆盖先到者 (约束 4)。
   * @return true = 注册成功; false = 拒绝
   */
  static Register(manifest) {
    if (!IsObject(manifest)) {
      this._recordError("", "Register rejected: manifest not an object")
      return false
    }
    id := manifest.Has("id") ? manifest["id"] : ""
    if (id == "") {
      this._recordError("", "Register rejected: missing 'id'")
      return false
    }
    if (!manifest.Has("entry") || manifest["entry"] == "") {
      this._recordError(id, "Register rejected: missing 'entry'")
      return false
    }
    if (this.Plugins.Has(id)) {
      this._recordError(id, "Register rejected: duplicate plugin (first wins)")
      return false
    }
    perms := manifest.Has("permissions") ? manifest["permissions"] : []
    err := this._validatePermissions(perms)
    if (err != "") {
      this._recordError(id, "Register rejected: " err)
      return false
    }
    this.Plugins[id] := Map("manifest", manifest, "enabled", true)
    return true
  }

  static Unregister(id) {
    if (this.Plugins.Has(id)) {
      this.Plugins.Delete(id)
      return true
    }
    return false
  }

  static Get(id) {
    return this.Plugins.Has(id) ? this.Plugins[id] : ""
  }

  /**
   * 按 manifest.permissions 裁剪的 API 视图 (契约 §3.7)。
   * 未知插件返回空串。
   */
  static GetAPI(id) {
    plugin := this.Get(id)
    if (plugin == "")
      return ""
    perms := plugin["manifest"].Has("permissions") ? plugin["manifest"]["permissions"] : []
    return APIBridge.Create(perms)
  }

  static _validatePermissions(perms) {
    if (!IsObject(perms))
      return "permissions not an array"
    for p in perms {
      if (!this._inList(p, PluginManager.PERMISSIONS))
        return "unknown permission '" p "'"
    }
    return ""
  }

  static _inList(needle, arr) {
    for x in arr
      if (x == needle)
        return true
    return false
  }

  static _recordError(pluginId, msg) {
    this.Errors.Push(Map("pluginId", pluginId, "message", msg))
    this._log(msg)
  }

  ; 错误日志 (与 ActionRegistry._log 同策略: 追加写, 失败静默)
  static _log(msg) {
    try {
      FileAppend(FormatTime(, "yyyy-MM-dd HH:mm:ss") " " msg "`n", "logs\plugin_manager.log")
    }
  }
}
