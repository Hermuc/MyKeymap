; ============================================================
; 选中动作系统 (Selected Action)
; 参考 RunAny 的「选中内容 + 快捷键触发预设行为」能力
; 配置由 config-server 渲染到生成的 MyKeymap.ahk 中:
;   ActionSchemeList := Array({id:1, name:"...", hotkey:"^+q", rules:Array(...)})
;   InitActionScheme(ActionSchemeList)
; 变量约定: 在命令或脚本中使用 {selected} 表示当前选中的文本或文件路径,
; %selected% 为兼容形 (由 context/SelectionContext.ahk 归一处理);
; 多文件用换行分隔, 与资源管理器复制文件到剪贴板的格式一致。
; 阶段2拆分说明: 本文件保留匹配层 (Match*) 与执行层 (Execute*/Run*);
; 选中内容获取已统一到 SelectionContext, 执行层未来 (阶段3) 委托 ActionRegistry。
; ============================================================

/**
 * 初始化选中动作方案: 为每个方案注册全局热键
 * @param schemes 方案数组
 */
InitActionScheme(schemes) {
  for scheme in schemes {
    if scheme.hotkey {
      ; 无效热键(如反引号)注册失败时跳过该方案, 避免单个方案拖垮整个脚本
      try {
        KeymapManager.GlobalKeymap.Map(scheme.hotkey, RunActionScheme.Bind(scheme), , , , "S")
      } catch {
        continue
      }
    }
  }
}

/**
 * 方案入口: 获取选中内容 -> 按优先级匹配规则 -> 执行行为
 * @param scheme 方案对象
 */
RunActionScheme(scheme) {
  selected := SelectionContext.Get()
  if not (selected.content) {
    Tip(Translation().no_items_selected, -700)
    return
  }

  for ruleIndex, rule in scheme.rules {
    if MatchActionRule(rule, selected) {
      if rule.options.confirm {
        if not (MsgBox("执行选中动作「" scheme.name "」?", "确认", "YesNo") == "Yes") {
          return
        }
      }
      ; 阶段 6: 慢事件广播 (薄观察层, 隔离兜底, 不影响动作执行)
      try EventBus.Publish("selection_action", Map("schemeId", scheme.id, "ruleIndex", ruleIndex, "selected", selected.content))
      ExecuteActionRule(rule, selected)
      ; 执行后处理: copyToClipboard 在动作执行后把选中内容保留到剪贴板 (便于执行完成后直接粘贴), 而非执行前
      if rule.options.copyToClipboard {
        A_Clipboard := selected.content
      }
      if rule.options.clearSelection {
        Send("{Esc}")
      }
      return
    }
  }
  ; 没有任何规则匹配时静默返回
}

/**
 * 获取当前选中内容 (阶段2: 实现已迁移到 context/SelectionContext.ahk, 保留函数签名兼容存量调用)
 * @deprecated 新代码请直接用 SelectionContext.Get(); 本壳保留是为兼容用户自定义代码 (data/custom_functions.ahk)
 *             与旧配置中的遗留引用, 勿删除。
 * @returns {{type: string, content: string}} type: file / text / ""
 */
GetSelectedContent() {
  return SelectionContext.Get()
}

/**
 * 判断选中内容是否匹配某条规则
 * @param rule 规则对象
 * @param selected 选中内容 {type, content}
 * @returns {boolean}
 */
MatchActionRule(rule, selected) {
  switch rule.matchType {
    case "fileExt":
      if selected.type != "file" {
        return false
      }
      return MatchFileExt(rule.matchValue, selected.content)
    case "fileGroup":
      if selected.type != "file" {
        return false
      }
      return MatchFileGroup(rule.matchValue, selected.content)
    case "textType":
      if selected.type != "text" {
        return false
      }
      return MatchTextType(rule.matchValue, selected.content)
    case "default":
      return true
  }
  return false
}

/**
 * 文件后缀匹配, 条件值支持逗号分隔多个后缀, "*" 匹配任意文件
 * @param matchValue 如 ".txt" / "txt,md" / "*"
 * @param content 文件路径列表 (换行分隔)
 * @returns {boolean}
 */
MatchFileExt(matchValue, content) {
  if matchValue == "*" {
    return true
  }
  exts := StrSplit(matchValue, ",")
  for line in StrSplit(content, "`n") {
    SplitPath(line, , , &ext)
    if not (ext) {
      continue
    }
    for v in exts {
      v := LTrim(Trim(v), ".")
      ; 扩展名比较忽略大小写 (与 Go 端 EqualFold 一致): Windows 上 .JPG/.PNG 等大写扩展名也必须能匹配
      if v == "*" || StrLower(v) == StrLower(ext) {
        return true
      }
    }
  }
  return false
}

/**
 * 文件类型分组匹配, 选中内容中的任意文件命中分组即匹配
 * 与 config-server/internal/script/actionscheme.go 中的分组表保持一致
 * @param group 分组名: image / doc / code / archive / video / audio
 * @param content 文件路径列表 (换行分隔)
 * @returns {boolean}
 */
MatchFileGroup(group, content) {
  exts := Map(
    "image", ["jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico"],
    "doc", ["doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf", "txt", "md"],
    "code", ["c", "cpp", "h", "hpp", "java", "py", "js", "ts", "jsx", "tsx", "go", "rs", "rb", "php", "html", "css", "scss", "json", "xml", "yml", "yaml", "sh", "bat"],
    "archive", ["zip", "rar", "7z", "tar", "gz", "bz2", "xz"],
    "video", ["mp4", "avi", "mkv", "mov", "wmv", "flv", "webm"],
    "audio", ["mp3", "wav", "flac", "ogg", "aac", "m4a"]
  )
  list := exts.Get(Trim(group), false)
  if not (list) {
    return false
  }
  for line in StrSplit(content, "`n") {
    SplitPath(line, , , &ext)
    if not (ext) {
      continue
    }
    for v in list {
      if StrLower(v) == StrLower(ext) {
        return true
      }
    }
  }
  return false
}

/**
 * 文本特征匹配: url(链接) / path(路径) / magnet(磁力链接) / plain(纯文本)
 * 与 config-server/internal/script/actionscheme.go 的 matchTextType 保持一致
 * @param t 特征类型
 * @param content 选中文本
 * @returns {boolean}
 */
MatchTextType(t, content) {
  isURL := RegExMatch(content, "i)^(https?|ftp)://") > 0
  isPath := RegExMatch(content, "^(\\\\[^\\]+\\[^\\]+|[a-zA-Z]:\\)") > 0
  isMagnet := RegExMatch(content, "i)^magnet:") > 0
  switch Trim(t) {
    case "url":
      return isURL
    case "path":
      return isPath
    case "magnet":
      return isMagnet
    case "plain":
      return not (isURL or isPath or isMagnet)
  }
  return false
}

/**
 * 执行规则对应的行为
 * textType 特征的专用行为 (open_url 等) 直接作用于选中内容, 不接受命令模板;
 * 特征与行为的合法组合见 config-server/internal/script/actionscheme.go 的 textTypeActions
 * @param rule 规则对象
 * @param selected 选中内容
 */
ExecuteActionRule(rule, selected) {
  content := selected.content
  switch rule.actionType {
    case "open_url":
      ; 默认浏览器打开选中网址 (AHK Run 对 http(s)/ftp URL 自动调用系统默认浏览器)
      Run(Trim(content))
    case "open_path":
      OpenSelectedPaths(content)
    case "open_folder":
      OpenSelectedFolder(content)
    case "magnet_download":
      DownloadMagnet(content)
    case "open_registry":
      OpenRegistryKey(content)
    case "open":
      RunReplaced(rule.actionValue, content, rule.workingDir)
    case "run":
      RunReplaced(rule.actionValue, content, rule.workingDir)
    case "search":
      url := SelectionContext.NormalizeForSearch(rule.actionValue, content)
      Run(url)
    case "send_keys":
      Send(SelectionContext.Normalize(rule.actionValue, content))
    case "script":
      RunScriptWithSelected(rule.actionValue, content)
    case "copy":
      A_Clipboard := SelectionContext.Normalize(rule.actionValue, content)
  }
}

/**
 * 打开选中路径 (逐行), 按系统关联程序打开, 等同资源管理器双击
 * @param content 路径列表 (换行分隔)
 */
OpenSelectedPaths(content) {
  for line in StrSplit(content, "`n") {
    line := Trim(line)
    if (line) {
      Run(QuoteIfSpace(line))
    }
  }
}

/**
 * 打开选中路径所在文件夹: 选中本身是目录时直接打开, 是文件时打开其父目录
 * 多选时只处理第一行 (行为语义: 打开第一个路径所在文件夹)
 * @param content 选中内容
 */
OpenSelectedFolder(content) {
  line := Trim(StrSplit(content, "`n")[1])
  if not (line) {
    return
  }
  ; FileExist 返回属性串, 含 "D" 表示目录
  if InStr(FileExist(line), "D") {
    Run(QuoteIfSpace(line))
    return
  }
  SplitPath(line, , &dir)
  if (dir) {
    Run(QuoteIfSpace(dir))
  }
}

/**
 * 用默认 BT 下载工具下载磁力链接 (走 magnet: 协议关联, 不硬编码具体下载软件)
 * 系统未注册默认处理器时给出中文提示而非静默失败
 * @param content 选中内容
 */
DownloadMagnet(content) {
  line := Trim(StrSplit(content, "`n")[1])
  if not (line) {
    return
  }
  if not (CheckMagnetHandler()) {
    Tip(Translation().magnet_no_handler, -2500)
    return
  }
  Run(line)
}

/**
 * 检测系统是否注册了 magnet: 协议默认处理器
 * HKCR 为 HKLM/HKCU 类注册的合并视图, 普通用户权限可读
 * @returns {boolean}
 */
CheckMagnetHandler() {
  try {
    cmd := RegRead("HKCR\magnet\shell\open\command")
    return cmd != ""
  }
  catch {
    return false
  }
}

/**
 * 打开注册表编辑器并定位到选中键路径
 * 原理: regedit 启动时读取 LastKey 值自动定位 (系统内置行为, 无需第三方工具与管理员权限)
 * regedit 已在运行时先结束再重开 (regedit 无未保存数据, 杀进程安全)
 * @param content 选中内容
 */
OpenRegistryKey(content) {
  path := Trim(StrSplit(content, "`n")[1])
  if not (path) {
    return
  }
  try RegWrite(path, "REG_SZ", "HKCU\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", "LastKey")
  catch {
    Tip(Translation().registry_open_failed, -2500)
    return
  }
  if ProcessExist("regedit.exe") {
    ProcessClose("regedit.exe")
    ProcessWaitClose("regedit.exe", 2)
  }
  Run("regedit.exe")
}

/**
 * 把 %selected% 替换为选中内容后执行命令, 多文件时逐行执行
 * 参考 RunAny: 占位符未带引号且内容含空格时自动包上双引号
 * @param command 命令模板
 * @param content 选中内容
 * @param workingDir 工作目录 (可选)
 */
RunReplaced(command, content, workingDir := "") {
  lines := StrSplit(content, "`n")
  if lines.Length == 1 {
    Run(SelectionContext.Normalize(command, QuoteIfSpace(lines[1])), workingDir)
    return
  }
  for line in lines {
    Run(SelectionContext.Normalize(command, QuoteIfSpace(line)), workingDir)
  }
}

/**
 * 内容含空格且未加引号时自动包上双引号 (参考 RunAny)
 * @param text 文本
 * @returns {string}
 */
QuoteIfSpace(text) {
  q := Chr(34)  ; 字面双引号: AHK v2 中 """" 会解析为两个空字符串, 必须用 Chr(34)
  if InStr(text, " ") and not (SubStr(text, 1, 1) == q) {
    return q text q
  }
  return text
}

/**
 * 执行 AHK 脚本片段: 把 %selected% 替换为字符串字面量, 写入临时脚本用 AutoHotkey 执行
 * @param code 脚本模板
 * @param content 选中内容
 */
RunScriptWithSelected(code, content) {
  file := A_WorkingDir "\data\selected_action_cache.ahk"
  script := StrReplace(code, "%selected%", ToAHKString(content))
  f := FileOpen(file, "w", "UTF-8")
  f.Write(script)
  f.Close()
  Run('"' A_WorkingDir '\bin\AutoHotkey64.exe" "' file '"', A_WorkingDir "\data")
}

/**
 * 转义为 AHK 双引号字符串字面量
 * 注意: 反引号+引号 (`` `" ``) 在字符串内是合法的转义, 但为避免歧义这里用 Chr(34) 构造双引号
 * @param text 文本
 * @returns {string}
 */
ToAHKString(text) {
  ; 反引号 → `` (双反引号)
  text := StrReplace(text, "``", "````")
  ; 双引号 → `" (反引号 + 双引号)
  text := StrReplace(text, Chr(34), "``" Chr(34))
  return Chr(34) text Chr(34)
}
