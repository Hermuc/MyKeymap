/**
 * translation.ahk —— 界面文案多语言 (中文/英文)。
 * DefaultTranslation 为英文基类, ChineseTranslation 继承并覆写中文文案;
 * Translation() 按系统语言缓存单例 (首次调用后固定, 不随运行中变化)。
 * 新增文案: 先在 DefaultTranslation 加英文, 再在 ChineseTranslation 加中文。
 */
class DefaultTranslation {
  mykeymap_on := "🚀  MyKeymap: On  "
  mykeymap_off := "⏸️  MyKeymap: Off  "
  
  menu_pause := "Pause"
  menu_exit := "Exit"
  menu_reload := "Reload"
  menu_settings := "Settings"
  menu_window_spy := "Window Spy"

  no_items_selected := "no items selected"
  always_on_top_on := "Always-on-top: On"
  always_on_top_off := "Always-on-top: Off"
  copy_failed := " Copy: fail "
  copy_ok := " Copy: ok "
  mute_on := "Mute: On"
  mute_off := "Mute: Off"
  mute_falied := "Cannot mute this app"
  app_running_in_background := "App is running in background, click the tray icon to show it"
}

class ChineseTranslation extends DefaultTranslation {
  mykeymap_on :=  "🚀  恢复 MyKeymap  "
  mykeymap_off := "⏸️  暂停 MyKeymap  "

  menu_pause := "暂停"
  menu_exit := "退出"
  menu_reload := "重启程序"
  menu_settings := "打开设置"
  menu_window_spy := "查看窗口标识符"

  no_items_selected := "没有选中的文本或文件"
  always_on_top_on := "置顶当前窗口"
  always_on_top_off := "取消置顶"
  copy_failed := "复制失败"
  copy_ok := "复制成功"
  mute_on := "静音当前应用"
  mute_off := "取消静音"
  mute_falied := "无法静音此应用"
  app_running_in_background := "程序在后台运行，请点击托盘图标唤出"
}


; 按系统语言选择翻译单例; static 缓存, 全进程只判断一次。
Translation() {
  static t := SysLangIsChinese() ? ChineseTranslation() : DefaultTranslation()
  return t
}

; A_Language 为区域标识十六进制串, 凡是中文系区域都视为中文 (见注释中的官方语言表链接)。
SysLangIsChinese()
{
  ; https://www.autohotkey.com/docs/v2/misc/Languages.htm
  m := Map(
    "7804", "Chinese",  ; zh
    "0004", "Chinese (Simplified)",  ; zh-Hans
    "0804", "Chinese (Simplified, China)",  ; zh-CN
    "1004", "Chinese (Simplified, Singapore)",  ; zh-SG
    "7C04", "Chinese (Traditional)",  ; zh-Hant
    "0C04", "Chinese (Traditional, Hong Kong SAR)",  ; zh-HK
    "1404", "Chinese (Traditional, Macao SAR)",  ; zh-MO
    "0404", "Chinese (Traditional, Taiwan)",  ; zh-TW
  )
  return m.Get(A_Language, false)
}