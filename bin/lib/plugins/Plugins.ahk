/**
 * Plugins.ahk —— plugins/ 聚合 include 入口 (与 actions/Actions.ahk 同模式)。
 * 单文件校验 / 接入时只需 include 本文件。
 * 阶段 5 框架: PluginManager / APIBridge / ScriptHost, 仅定义不接入运行路径。
 */
#Include APIBridge.ahk
#Include PluginManager.ahk
#Include ScriptHost.ahk
