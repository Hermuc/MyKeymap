export interface Action {
  isEmpty: boolean
  windowGroupID: number
  actionTypeID: number
  comment?: string

  actionValueID?: number

  remapToKey?: string
  keysToSend?: string
  ahkCode?: string

  winTitle?: string
  target?: string
  args?: string
  workingDir?: string
  runAsAdmin?: boolean
  runInBackground?: boolean
  detectHiddenWindow?: boolean

}
export interface Keymap {
  id: number
  name: string
  enable: boolean
  hotkey: string
  parentID: number
  delay: number
  isNew?: boolean
  hotkeys: {
    [key: string]: Array<Action>
  }
}

export interface Scroll {
  delay1: string
  delay2: string
  onceLineCount: string;
}

export interface Mouse {
  delay1: string
  delay2: string
  fastSingle: string
  fastRepeat: string
  slowSingle: string
  slowRepeat: string
  keepMouseMode: boolean
  showTip: boolean
  tipSymbol: string
}

export interface WindowGroup {
  id: number
  name: string
  value: string
  conditionType?: number
}

export type PathVariable = {
  name: string
  value: string
}

export interface Options {
  mykeymapVersion: string
  scroll: Scroll
  mouse: Mouse
  windowGroups: Array<WindowGroup>
  pathVariables: Array<PathVariable>
  customShellMenu: string
  startup: boolean
  language: string
  keyMapping: string
  keyboardLayout: string
  commandInputSkin: any
}

export interface Config {
  keymaps: Array<Keymap>
  options: Options
  actionSchemes?: Array<ActionScheme>
  // 自定义帮助页 HTML, 保存后生成 bin/site/help.html (通过 MyKeymap 动作 openHelpHtml 打开)
  helpPageHtml?: string
}

// ===== 选中动作方案 =====
// 选中文本/文件后按下快捷键执行预设行为, 变量 %selected% 表示当前选中的文本或文件路径
export interface ActionScheme {
  id: number
  name: string
  hotkey: string
  enable: boolean
  rules: Array<ActionRule>
}

// 规则按 priority 升序匹配, 第一个匹配的规则生效
export interface ActionRule {
  priority: number
  // matchType: fileExt(文件后缀) / fileGroup(文件分组) / textType(文本特征) / default(兜底)
  matchType: string
  matchValue: string
  // actionType: open(程序打开) / search(搜索) / run(执行命令) / send_keys(发送按键) / script(AHK脚本) / copy(复制到剪贴板)
  //   + textType 专用: open_url / open_path / open_folder / magnet_download / open_registry (见 constants.ts TEXT_TYPE_ACTIONS)
  actionType: string
  actionValue: string
  workingDir?: string
  options: RuleOptions
}

export interface RuleOptions {
  copyToClipboard: boolean
  clearSelection: boolean
  confirm: boolean
}
