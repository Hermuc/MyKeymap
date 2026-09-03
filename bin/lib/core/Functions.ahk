/**
 * Functions.ahk —— 托盘菜单/生命周期 + 选中文本与剪贴板 + 编码杂项
 * (均为慢路径辅助, 不参与重映射/发键/鼠标快路径)。
 * 2026-09-03 按职责拆分 (函数体逐行搬运未修改):
 *   程序启动与进程 -> core/Programs.ahk | 窗口查找激活 -> core/WindowUtils.ahk |
 *   命令输入框消息 -> core/AbbrInput.ahk。
 * 兼容壳: GetSelectedText 实现已迁移至 context/SelectionContext.ahk, 仅保留签名。
 */

/**
 * 托盘菜单被点击
 * @param ItemName 
 * @param ItemPos 
 * @param MyMenu 
 */
TrayMenuHandler(ItemName, ItemPos, MyMenu) {
  ; 注: 不用 switch 是因为 2.0.19 解释器无法编译 case 表达式含函数调用/属性访问
  t := Translation()
  if (ItemName == t.menu_exit) {
    MyKeymapExit()
  } else if (ItemName == t.menu_pause) {
    MyKeymapToggleSuspend()
  } else if (ItemName == t.menu_reload) {
    MyKeymapReload()
  } else if (ItemName == t.menu_settings) {
    MyKeymapOpenSettings()
  } else if (ItemName == t.menu_window_spy) {
    run("MyKeymap.exe /script bin\WindowSpy.ahk")
  }
}

/**
 * 退出程序
 * @param ExitReason 退出原因
 * @param ExitCode 传递给 Exit 或 ExitApp 的退出代码.
 */
MyKeymapExit(ExitReason?, ExitCode?) {
  ProcessClose("MyKeymap-CommandInput.exe")
  ExitApp
}

/**
 * 暂停
 */
MyKeymapToggleSuspend() {
  fn() {
    Suspend(!A_IsSuspended)
    if (A_IsSuspended) {
      TraySetIcon("./bin/icons/logo2.ico")
      A_TrayMenu.Check(Translation().menu_pause)
      Tip(Translation().mykeymap_off, -500)
    } else {
      TraySetIcon("./bin/icons/logo.ico")
      A_TrayMenu.UnCheck(Translation().menu_pause)
      Tip(Translation().mykeymap_on, -500)
    }
  }

  if A_PriorKey == "RButton" {
    SetTimer(fn, -200)
    return
  }
  fn()
}

/**
 * 打开设置 (原生 Avalonia GUI: bin\ui\MyKeymap.Settings.exe, 与 settings.exe 同处 bin\ 体系,
 * 窗口标题 "MyKeymap Setting"; 不再经 wt.exe 承载, 后端由 GUI 以子进程管理)
 */
MyKeymapOpenSettings() {
  launchSettings() {
    Run('"' A_ScriptDir '\ui\MyKeymap.Settings.exe"', A_ScriptDir)
  }
  if (!ProcessExist("MyKeymap.Settings.exe")) {
    launchSettings()
  } else if (WinExist("MyKeymap Setting")) {
    WinActivate("MyKeymap Setting")
  } else {
    ; 进程存在但设置窗口不可见, 重启设置程序 (其会一并重建后端子进程)
    if ProcessExist("MyKeymap.Settings.exe") {
      ProcessClose("MyKeymap.Settings.exe")
      ProcessWaitClose("MyKeymap.Settings.exe", 2)
    }
    launchSettings()
  }
}

/**
 * 重启程序
 */
MyKeymapReload() {
  Tip("Reload")
  Run("MyKeymap.exe")
}

/**
 *  将程序路径或参数中的{selected} 替换为选中的文字
 * @param target 程序路径的引用
 * @param args 参数的引用
 * @returns {void|number} 
 */
ReplaceSelectedText(&target, &args) {
  text := GetSelectedText()
  if not (text) {
    text := ""
  }

  ; 如果是划词搜索且选中了 http 链接那么跳转链接
  if InStr(args, "https://") == 1 || InStr(target, "https://") == 1 {
    if InStr(text, "https://") || InStr(text, "http://") {
      args := InStr(args, "{selected}") ? Trim(text) : args
      target := InStr(target, "{selected}") ? Trim(text) : target
      return 1
    }
  }

  if InStr(args, "://") || InStr(target, "://") {
    text := URIEncode(text)
  }
  args := strReplace(args, "{selected}", text)
  target := strReplace(target, "{selected}", text)

  return 1
}

/**
 * 获取选中的文字 (阶段2: 实现已迁移到 context/SelectionContext.ahk, 保留函数签名兼容存量调用)
 * @returns {void|string} 
 */
GetSelectedText() {
  sel := SelectionContext.Get(false)
  if not (sel.content) {
    return
  }
  return sel.content
}

/**
 * url 编码
 * 来源: https://www.autohotkey.com/boards/viewtopic.php?t=112741
 * @param Uri 需要编码的文本
 * @param {string} encoding 编码格式
 * @returns {string} 
 */
URIEncode(Uri, encoding := "UTF-8") {
  res := ""
  var := Buffer(StrPut(Uri, encoding), 0)
  StrPut(Uri, var, encoding)
  pos := 1
  ; 按字节遍历 buffer 中的 utf-8 字符串, 注意字符串有  null-terminator
  While pos < var.Size {
    code := NumGet(var, pos - 1, "UChar")
    if (code >= 0x30 && code <= 0x39) || (code >= 0x41 && code <= 0x5A) || (code >= 0x61 && code <= 0x7A)
      res .= Chr(code)
    else
      res .= "%" . Format("{:02X}", code)
    pos++
  }
  return res
}

/**
 * 将文本转换为Html
 * @param text 需要转换的文本
 * @param color HEX颜色值
 * @param fontFamily 字体
 * @returns {string} 
 */
FormatHtmlStyle(text, color, fontFamily) {
  style := "Color: '" color "'; font-family: '" fontFamily ";"

  text := HtmlEncode(text)
  html := "<HTML> <head><meta http-equiv='Content-type' content='text/html;charset=UTF-8'></head> <body> <!--StartFragment-->"
  if (InStr(text, "`n")) {
    html .= "<span style='" style "'><pre>" text "</pre></span>"
  } else {
    html .= "<span style='" style "'>" text "</span>"
  }
  html .= "<!--EndFragment--></body></HTML>"
  return html
}

/**
 * Html编码
 * @param text 需要编码的文本
 * @returns {void} 
 */
HtmlEncode(text) {
  text := strReplace(text, "&", "&amp;")
  text := strReplace(text, "<", "&lt;")
  text := strReplace(text, ">", "&gt;")
  ; 注意: 双引号必须用 Chr(34), 写 "" 会被词法器解析为空字符串, 导致替换失效
  text := strReplace(text, Chr(34), "&quot;")
  text := strReplace(text, " ", "&nbsp;")
  return text
}

/**
 * 将文本粘贴到当前程序中
 * @param text 文本
 */
PasteToPrograms(text) {
  A_Clipboard := text
  Send("{LShift down}{Insert down}{Insert up}{LShift up}")
}
