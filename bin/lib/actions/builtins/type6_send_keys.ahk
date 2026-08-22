/**
 * TypeID 6: 发送按键辅助 (按住修饰键系列)
 * 拆自 Actions.ahk (模块化重构阶段 3), 函数体逐行搬运未做任何修改。
 */

/**
 * 按住左Shift
 */
HoldDownLShiftKey() {
  send("{LShift down}")
  key := ExtractWaitKey(A_ThisHotkey)
  keywait(key)
  send("{LShift up}")
}


HoldDownModifierKey(modifier) {
  send("{" modifier " down}")
  key := ExtractWaitKey(A_ThisHotkey)
  keywait(key)
  send("{" modifier " up}")
}

/**
 * 按住右Shift
 */
HoldDownRShiftKey() {
  send("{RShift down}")
  key := LTrim(A_ThisHotkey, "*")
  keywait(key)
  send("{RShift up}")
}
