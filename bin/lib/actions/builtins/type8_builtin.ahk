/**
 * TypeID 8: 内置函数
 * 拆自 Actions.ahk (模块化重构阶段 3), 函数体逐行搬运未做任何修改。
 */

/**
 * 查看帮助
 */
openHelpHtml() {
  if FileExist("bin\site\help.html") {
    Run("bin\site\help.html")
  } else {
    MsgBox("帮助文件未生成，需要打开设置点一下保存")
  }
}
