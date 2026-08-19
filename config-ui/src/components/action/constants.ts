// 选中动作系统的常量与工具函数

// 匹配条件类型
export const MATCH_TYPES = [
  { value: "fileExt", label: "文件后缀", hint: "如 .txt 或 txt,md (逗号分隔多个), * 匹配任意文件" },
  { value: "fileGroup", label: "文件分组", hint: "image / doc / code / archive / video / audio" },
  { value: "textRegex", label: "文本正则", hint: "如 ^https?:// 或 ^[a-zA-Z]:\\\\" },
  { value: "textType", label: "文本特征", hint: "url (链接) / path (路径) / plain (纯文本)" },
  { value: "default", label: "默认 (兜底)", hint: "任何内容都匹配, 一般放在规则列表最后" },
]

// 行为类型
export const ACTION_TYPES = [
  { value: "open", label: "程序打开", hint: "用指定程序打开选中文件, 如 code.exe %selected%" },
  { value: "search", label: "搜索", hint: "把 %selected% 作为关键词搜索, 如 https://www.google.com/search?q=%selected%" },
  { value: "run", label: "执行命令", hint: "执行命令, 支持 %selected% 变量, 如 notepad.exe %selected%" },
  { value: "send_keys", label: "发送按键", hint: "向当前窗口发送按键序列, 如 ^c; 含选中内容时建议用 {text}%selected% 包裹, 否则内容中的 {Enter} 等会被解析为按键" },
  { value: "script", label: "AHK 脚本", hint: "执行一段 AutoHotkey 脚本片段, %selected% 会替换为字符串字面量" },
  { value: "copy", label: "复制", hint: "把 %selected% 替换后的文本写入剪贴板" },
]

// 文件分组 (与后端 actionscheme.go / AHK SelectedAction.ahk 保持一致)
export const FILE_GROUPS = [
  { value: "image", label: "图片", exts: "jpg jpeg png gif bmp webp svg ico" },
  { value: "doc", label: "文档", exts: "doc docx xls xlsx ppt pptx pdf txt md" },
  { value: "code", label: "代码", exts: "c cpp h hpp java py js ts jsx tsx go rs rb php html css scss json xml yml yaml sh bat" },
  { value: "archive", label: "压缩包", exts: "zip rar 7z tar gz bz2 xz" },
  { value: "video", label: "视频", exts: "mp4 avi mkv mov wmv flv webm" },
  { value: "audio", label: "音频", exts: "mp3 wav flac ogg aac m4a" },
]

// 文本特征
export const TEXT_TYPES = [
  { value: "url", label: "链接" },
  { value: "path", label: "路径" },
  { value: "plain", label: "纯文本" },
]

// 默认搜索模板
export const DEFAULT_SEARCH_URL = "https://www.google.com/search?q=%selected%"

// 新建一个空规则 (priority 由外部指定)
export function createRule(priority: number) {
  return {
    priority,
    matchType: "fileExt",
    matchValue: "",
    actionType: "open",
    actionValue: "",
    workingDir: "",
    options: {
      copyToClipboard: false,
      clearSelection: false,
      confirm: false,
    },
  }
}

// 新建一个默认方案 (id 由后端分配)
export function createScheme() {
  return {
    id: 0,
    name: "新的选中动作",
    hotkey: "",
    enable: true,
    rules: [createRule(1)],
  }
}

// AHK 修饰键前缀 -> 显示名
const MODIFIER_MAP: { [key: string]: string } = {
  "^": "Ctrl",
  "!": "Alt",
  "+": "Shift",
  "#": "Win",
  "<^": "LCtrl",
  "<!": "LAlt",
  "<+": "LShift",
  "<#": "LWin",
  ">^": "RCtrl",
  ">!": "RAlt",
  ">+": "RShift",
  ">#": "RWin",
}

// AHK 格式热键 -> 可读格式 (^+q -> Ctrl+Shift+Q)
export function ahkToDisplay(ahk: string) {
  if (!ahk) return ""
  const parts: string[] = []
  let rest = ahk
  // 依次提取修饰键前缀
  while (rest.length > 0 && MODIFIER_MAP[rest.substring(0, 2)]) {
    parts.push(MODIFIER_MAP[rest.substring(0, 2)])
    rest = rest.substring(2)
  }
  while (rest.length > 0 && MODIFIER_MAP[rest.substring(0, 1)]) {
    parts.push(MODIFIER_MAP[rest.substring(0, 1)])
    rest = rest.substring(1)
  }
  // 剩余部分: 去掉 * ~ $ 等前缀
  rest = rest.replace(/^[*~$]+/, "")
  if (rest) {
    parts.push(keyToDisplay(rest))
  }
  return parts.join("+")
}

// AHK 键名 -> 显示名
function keyToDisplay(key: string) {
  const map: { [key: string]: string } = {
    space: "Space",
    esc: "Esc",
    enter: "Enter",
    tab: "Tab",
    up: "↑",
    down: "↓",
    left: "←",
    right: "→",
    pgup: "PageUp",
    pgdn: "PageDown",
    home: "Home",
    end: "End",
    ins: "Insert",
    del: "Delete",
    backspace: "Backspace",
    apps: "Menu",
  }
  return map[key.toLowerCase()] ?? key.charAt(0).toUpperCase() + key.slice(1)
}

// 修饰键物理位置 (KeyboardEvent.code) -> AHK 前缀与显示名
// 浏览器实测确认: 修饰键 keydown 的 e.key 为 Control/Alt/Shift/Meta,
// 左右区分必须依赖 e.code (ControlLeft/ControlRight 等)
export const MODIFIER_CODE_MAP: { [code: string]: { prefix: string, label: string } } = {
  ControlLeft:  { prefix: "<^", label: "LCtrl" },
  ControlRight: { prefix: ">^", label: "RCtrl" },
  AltLeft:      { prefix: "<!", label: "LAlt" },
  AltRight:     { prefix: ">!", label: "RAlt" },
  ShiftLeft:    { prefix: "<+", label: "LShift" },
  ShiftRight:   { prefix: ">+", label: "RShift" },
  MetaLeft:     { prefix: "<#", label: "LWin" },
  MetaRight:    { prefix: ">#", label: "RWin" },
}

// 生成 AHK 热键时的修饰键固定顺序 (AHK 中修饰符顺序不影响功能, 固定顺序保证可读性)
const MOD_ORDER = ["<^", ">^", "<!", ">!", "<+", ">+", "<#", ">#"]

// 根据修饰键 code 列表 + 主键生成 AHK 格式热键, 如 ["ControlLeft"] + "q" -> "<^q"
export function buildAhkFromCodes(modCodes: string[], mainKey: string): string {
  const prefixes = modCodes
    .map(c => MODIFIER_CODE_MAP[c]?.prefix)
    .filter(Boolean)
    .sort((a, b) => MOD_ORDER.indexOf(a) - MOD_ORDER.indexOf(b))
  return prefixes.join("") + mainKey
}

// 键盘事件 -> AHK 键名
export function keyToAhkName(e: KeyboardEvent): string | null {
  const key = e.key
  if (/^[a-zA-Z0-9]$/.test(key)) {
    return key.toLowerCase()
  }
  const map: { [key: string]: string } = {
    " ": "space",
    Escape: "esc",
    Enter: "enter",
    Tab: "tab",
    ArrowUp: "up",
    ArrowDown: "down",
    ArrowLeft: "left",
    ArrowRight: "right",
    Home: "home",
    End: "end",
    PageUp: "pgup",
    PageDown: "pgdn",
    Insert: "ins",
    Delete: "del",
    Backspace: "backspace",
    // 反引号键无 AHK 键名 (Hotkey("``") 报 Invalid hotkey), 不支持作为热键, 捕获时忽略该键
    "-": "-",
    "=": "=",
    "[": "[",
    "]": "]",
    "\\": "\\",
    ";": ";",
    "'": "'",
    ",": ",",
    ".": ".",
    "/": "/",
  }
  if (/^F\d{1,2}$/.test(key)) {
    return key
  }
  return map[key] ?? null
}

// 方案热键是否与现有 keymaps 冲突 (收集所有已注册的热键)
export function collectUsedHotkeys(keymaps: Array<any>): Set<string> {
  const used = new Set<string>()
  for (const km of keymaps) {
    if (!km.enable) continue
    for (const hk of Object.keys(km.hotkeys ?? {})) {
      used.add(normalizeHotkey(hk))
    }
    if (km.hotkey && km.hotkey != "settings" && km.hotkey != "customHotkeys") {
      used.add(normalizeHotkey(km.hotkey))
    }
  }
  return used
}

// 归一化热键用于比较: 去掉 * ~ $ 以及左右修饰前缀 < >, 忽略大小写
// 说明: <^q / >^q / ^q 归一化后均为 ^q, 冲突检测采用保守策略 (宁多报不放过)
export function normalizeHotkey(hk: string) {
  return hk.replace(/^[*~$<>]+/, "").toLowerCase()
}
