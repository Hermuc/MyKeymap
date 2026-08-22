/**
 * TypeID 4: 鼠标操作 (主体为 KeymapManager 的 MouseKeymap 方法, 此处为独立函数)
 * 拆自 Actions.ahk (模块化重构阶段 3), 函数体逐行搬运未做任何修改。
 */

/**
 * 移动鼠标到活动窗口中心
 */
MouseToActiveWindowCenter() {
  WinGetPos(&X, &Y, &W, &H, "A")
  MouseMove(x + w / 2, y + h / 2)
}

/**
 * 将鼠标移动到光标的位置
 */
MoveMouseToCaret() {
  GetCaretPos(&x, &y)
  if (StrLen(x) | StrLen(y)) {
    ; Tip(A_SendMode "|" A_CoordModeMouse) ; 每次执行热键, 这两个都会重置为默认值
    SendMode("Event")
    CoordMode("Mouse", "Screen")
    MouseMove(x, y, 100)
  } else {
    MouseToActiveWindowCenter()
  }
}
