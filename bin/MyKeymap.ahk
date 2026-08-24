#Requires AutoHotkey v2.0
#SingleInstance Force
#UseHook true

#include lib/core/translation.ahk
#Include lib/core/IKeyEventBus.ahk
#Include lib/core/EventBus.ahk
#Include lib/core/Functions.ahk
#Include lib/actions/Actions.ahk
#Include lib/core/KeymapManager.ahk
#Include lib/core/InputTipWindow.ahk
#Include lib/core/Utils.ahk
#Include lib/context/SelectionContext.ahk
#Include lib/rules/SelectedAction.ahk
#Include lib/commands/CommandResolver.ahk

; #WinActivateForce   ; 先关了遇到相关问题再打开试试
; InstallKeybdHook    ; 这个可以重装 keyboard hook, 提高自己的 hook 优先级, 以后可能会用到
; ListLines False     ; 也许能提升一点点性能 ( 别抱期待 ), 当有这个需求时再打开试试
; #Warn All, Off      ; 也许能提升一点点性能 ( 别抱期待 ), 当有这个需求时再打开试试

try DllCall("SetThreadDpiAwarenessContext", "ptr", -3, "ptr") ; 多显示器不同缩放比例会导致问题: https://www.autohotkey.com/boards/viewtopic.php?f=14&t=13810
SetMouseDelay 0                                           ; SendInput 可能会降级为 SendEvent, 此时会有 10ms 的默认 delay
SetWinDelay 0                                             ; 默认会在 activate, maximize, move 等窗口操作后睡眠 100ms
A_MaxHotkeysPerInterval := 256                            ; 默认 70 可能有点低, 即使没有热键死循环也触发警告
SendMode "Event"                                          ; 执行 SendInput 的期间会短暂卸载 Hook, 这时候松开引导键会丢失 up 事件, 所以 Event 模式更适合 MyKeymap
SetKeyDelay 0                                             ; 默认 10 太慢了, https://www.reddit.com/r/AutoHotkey/comments/gd3z4o/possible_unreliable_detection_of_the_keyup_event/
ProcessSetPriority "High"
SetWorkingDir("../")
InitTrayMenu()
InitKeymap()
OnExit(MyKeymapExit)
#include ../data/custom_functions.ahk

InitKeymap()
{
  taskSwitch := TaskSwitchKeymap("e", "d", "s", "f", "c", "space")
  mouseTip := false
  slow := MouseKeymap("slow mouse", false, mouseTip, 10, 13, "T0.13", "T0.01", 1, "T0.2", "T0.03")
  fast := MouseKeymap("fast mouse", false, mouseTip, 110, 70, "T0.13", "T0.01", 1, "T0.2", "T0.03", slow)
  slow.Map("*space", slow.LButtonUp())

  capsHook := InputHook("", "{CapsLock}{Esc}", "cw,dl,dm,et,ex,fc,fe,gd,hm,jy,ka,kp,kzm,ls,ly,me,mm,ob,pd,pp,qq,rb,rd,re,sd,se,sl,steam,tb,tg,tm,tu,wt,wx,zg")
  capsHook.KeyOpt("{CapsLock}", "S")
  capsHook.KeyOpt("{Backspace}", "N")
  capsHook.OnChar := (ih, char) => (PostCharToCaspAbbr(ih, char), FuzzySuffixFire(ih, char, "capslock"))
  capsHook.OnKeyDown := PostBackspaceToCaspAbbr
  Run("bin\MyKeymap-CommandInput.exe")


  ; 路径变量
  programs := "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\"

  ; 缩写命令注册表 (阶段 4: 取代 ExecCapslockAbbr 内的 switch)
  CommandResolver.Register("capslock", "cw", [CommandStep(() => SmartCloseWindow())])
  CommandResolver.Register("capslock", "dl", [CommandStep(() => ActivateOrRun("", "shell:downloads"))])
  CommandResolver.Register("capslock", "dm", [CommandStep(() => ActivateOrRun("", A_WorkingDir))])
  CommandResolver.Register("capslock", "et", [CommandStep(() => ActivateOrRun("Everything", "D:\PortableApps\Scoop\apps\tubatools\current\app\src\Tools\其他工具\Everything\everything.exe"))])
  CommandResolver.Register("capslock", "ex", [CommandStep(() => MyKeymapExit())])
  CommandResolver.Register("capslock", "fc", [CommandStep(() => ActivateOrRun("ahk_exe FlClash.exe", "shortcuts\FlClash.lnk", "", "", false, true, false))])
  CommandResolver.Register("capslock", "fe", [CommandStep(() => ActivateOrRun("ahk_exe explorer.exe", "shortcuts\File Explorer.lnk"))])
  CommandResolver.Register("capslock", "gd", [CommandStep(() => ActivateOrRun("Ghost Downloader", "shortcuts\Ghost Downloader 3.lnk"))])
  CommandResolver.Register("capslock", "hm", [CommandStep(() => ActivateOrRun("HypoMux", "shortcuts\HypoMux.lnk"))])
  CommandResolver.Register("capslock", "jy", [CommandStep(() => ActivateOrRun("剪映专业版", "D:\PortableApps\Unofficial\剪映专业版 v6.0.1\JianyingPro.exe"))])
  CommandResolver.Register("capslock", "ka", [CommandStep(() => ActivateOrRun("ahk_exe KugouAvaloniaPlayer.exe", "shortcuts\KugouAvaloniaPlayer.lnk"))])
  CommandResolver.Register("capslock", "kp", [CommandStep(() => CloseWindowProcesses())])
  CommandResolver.Register("capslock", "kzm", [CommandStep(() => ActivateOrRun("Kazumi", "shortcuts\kazumi.lnk"))])
  CommandResolver.Register("capslock", "ls", [CommandStep(() => ActivateOrRun("Lossless Scaling", "D:\PortableApps\Unofficial\LS.v3.2.1\LosslessScaling.exe"))])
  CommandResolver.Register("capslock", "ly", [CommandStep(() => ActivateOrRun("", "ms-settings:bluetooth"))])
  CommandResolver.Register("capslock", "me", [CommandStep(() => ActivateOrRun("ahk_exe msedge.exe", "shortcuts\Microsoft Edge.lnk"))])
  CommandResolver.Register("capslock", "mm", [CommandStep(() => ActivateOrRun("MyKeymap2 - Visual Studio Code", "shortcuts\Visual Studio Code.lnk", "D:\MyFiles\MyKeymap2", "", false, false, false))])
  CommandResolver.Register("capslock", "ob", [CommandStep(() => ActivateOrRun("ahk_exe Obsidian.exe", "shortcuts\Obsidian.lnk"))])
  CommandResolver.Register("capslock", "pd", [CommandStep(() => ShowActiveProcessInFolder())])
  CommandResolver.Register("capslock", "pp", [CommandStep(() => ActivateOrRun("PiliPlus", "shortcuts\PiliPlus.lnk"))])
  CommandResolver.Register("capslock", "qq", [CommandStep(() => ActivateOrRun("QQ", "shortcuts\QQ.lnk"))])
  CommandResolver.Register("capslock", "rb", [CommandStep(() => ActivateOrRun("", "shell:RecycleBinFolder"))])
  CommandResolver.Register("capslock", "rd", [CommandStep(() => Send("#d"))])
  CommandResolver.Register("capslock", "re", [CommandStep(() => SystemRestartExplorer())])
  CommandResolver.Register("capslock", "sd", [CommandStep(() => SystemShutdown())])
  CommandResolver.Register("capslock", "se", [CommandStep(() => MyKeymapOpenSettings())])
  CommandResolver.Register("capslock", "sl", [CommandStep(() => SystemSleep())])
  CommandResolver.Register("capslock", "steam", [CommandStep(() => ActivateOrRun(" ahk_exe steamwebhelper.exe", "shortcuts\Steam.lnk"))])
  CommandResolver.Register("capslock", "tb", [CommandStep(() => ActivateOrRun("图吧工具箱", "D:\PortableApps\Scoop\apps\tubatools\current\app\src\TubaWinUi3.exe"))])
  CommandResolver.Register("capslock", "tg", [CommandStep(() => ActivateOrRun("ahk_exe AyuGram.exe", "shortcuts\AyuGram.lnk"))])
  CommandResolver.Register("capslock", "tm", [CommandStep(() => Send("^+{esc}"))])
  CommandResolver.Register("capslock", "tu", [CommandStep(() => ActivateOrRun("Total Uninstall 专业版", "D:\PortableApps\Unofficial\TotalUninstall\TUPortable.exe"))])
  CommandResolver.Register("capslock", "wt", [CommandStep(() => ActivateOrRun("ahk_exe WindowsTerminal.exe", "wt.exe", "-d `"{selected}`"", "", false, false, false))])
  CommandResolver.Register("capslock", "wx", [CommandStep(() => ActivateOrRun("微信", "shortcuts\WeChat.lnk"))])
  CommandResolver.Register("capslock", "zg", [CommandStep(() => ActivateOrRun("ahk_class Zed::Window", "shortcuts\ZedG.lnk"))])
  ; 窗口组
  GroupAdd("MY_WINDOW_GROUP__1", "Stardew Valley ahk_class SDL_app")
  GroupAdd("MY_WINDOW_GROUP__1", "ahk_exe Rune Factory 3 Special.exe")
  GroupAdd("MY_WINDOW_GROUP_1", "ahk_exe chrome.exe")
  GroupAdd("MY_WINDOW_GROUP_1", "ahk_exe msedge.exe")
  GroupAdd("MY_WINDOW_GROUP_1", "ahk_exe firefox.exe")

  KeymapManager.GlobalKeymap.DisabledAt := "ahk_group MY_WINDOW_GROUP__1"

  ; CapsLock
  km5 := KeymapManager.NewKeymap("*CapsLock", "CapsLock", "", "")
  km := km5
  km.Map("*c", _ => SoundControl())
  km.Map("*z", _ => CopySelectedAsPlainText())
  km.Map("*.", _ => MakeWindowDraggable())
  km.Map("*a", _ => CenterAndResizeWindow(1370, 930))
  km.Map("*b", _ => MinimizeWindow())
  km.Map("*e", _ => Send("^!{tab}"), taskSwitch)
  km.Map("*g", _ => ToggleWindowTopMost())
  km.Map("*p", _ => GoToNextVirtualDesktop())
  km.Map("*q", _ => MaximizeWindow())
  km.Map("*r", _ => LoopRelatedWindows())
  km.Map("*s", _ => CenterAndResizeWindow(1200, 800))
  km.Map("*t", BindWindow())
  km.Map("*v", _ => MoveWindowToNextMonitor())
  km.Map("*w", _ => GoToLastWindow())
  km.Map("*x", _ => SmartCloseWindow())
  km.Map("*y", _ => GoToPreviousVirtualDesktop())
  km.Map("*,", fast.LButtonDown()), slow.Map("*,", slow.LButtonDown())
  km.Map("*/", _ => MoveMouseToCaret()), slow.Map("*/", _ => MoveMouseToCaret())
  km.Map("*;", fast.ScrollWheelRight), slow.Map("*;", slow.ScrollWheelRight)
  km.Map("*h", fast.ScrollWheelLeft), slow.Map("*h", slow.ScrollWheelLeft)
  km.Map("*i", fast.MoveMouseUp, slow), slow.Map("*i", slow.MoveMouseUp)
  km.Map("*j", fast.MoveMouseLeft, slow), slow.Map("*j", slow.MoveMouseLeft)
  km.Map("*k", fast.MoveMouseDown, slow), slow.Map("*k", slow.MoveMouseDown)
  km.Map("*l", fast.MoveMouseRight, slow), slow.Map("*l", slow.MoveMouseRight)
  km.Map("*m", fast.RButton()), slow.Map("*m", slow.RButton())
  km.Map("*n", fast.LButton()), slow.Map("*n", slow.LButton())
  km.Map("*o", fast.ScrollWheelDown), slow.Map("*o", slow.ScrollWheelDown)
  km.Map("*u", fast.ScrollWheelUp), slow.Map("*u", slow.ScrollWheelUp)
  km.Map("*0", _ => (Send("{home}+{end}{backspace}"), Send("{text}i love homura and hikari"), Sleep(1000), Send("{enter}yes{enter}")))
  km.Map("*d", _ => CenterAndResizeWindow(1740, 1000))
  km.Map("singlePress", _ => EnterCapslockAbbr(capsHook))

  ; J 模式
  km8 := KeymapManager.NewKeymap("*j", "J 模式", "", "")
  km := km8
  km.Map("*i", _ => (Send("{blind}ji")))
  km.Map("singlePress", _ => (Send("{blind}{j}")))
  km.RemapKey(",", "delete")
  km.RemapKey(".", "insert")
  km.Map("*2", _ => (Send("^+{tab}")))
  km.Map("*3", _ => (Send("^{tab}")))
  km.RemapKey("a", "home")
  km.Map("*b", _ => (Send("^{backspace}")))
  km.RemapKey("c", "backspace")
  km.Map("*d", _ => (Send("{blind}+{down}")))
  km.Map("*e", _ => (Send("{blind}+{up}")))
  km.Map("*f", _ => (Send("{blind}+{right}")))
  km.RemapKey("g", "end")
  km.Map("*k", _ => HoldDownModifierKey("LShift"))
  km.RemapKey("q", "appskey")
  km.RemapKey("r", "tab")
  km.Map("*s", _ => (Send("{blind}+{left}")))
  km.Map("*t", _ => (Send("{home}+{end}{backspace}")))
  km.Map("*v", _ => (Send("{blind}^{right}")))
  km.Map("*w", _ => (Send("{blind}+{tab}")))
  km.RemapKey("x", "esc")
  km.Map("*z", _ => (Send("{blind}^{left}")))
  km.Map("*space", _ => (Send("{blind}{enter}")))

  ; F 模式
  km9 := KeymapManager.NewKeymap("f", "F 模式", "0.100", "")
  km := km9
  km.Map("singlePress", _ => (Send("{blind}{f}")))
  km.RemapKey(",", "delete")
  km.RemapKey(".", "insert")
  km.RemapKey(";", "end")
  km.RemapKey("b", "backspace")
  km.RemapKey("e", "esc")
  km.RemapKey("h", "home")
  km.RemapKey("i", "up")
  km.RemapKey("j", "left")
  km.RemapKey("k", "down")
  km.RemapKey("l", "right")
  km.Map("*m", _ => (Send("{blind}^{right}")))
  km.Map("*n", _ => (Send("{blind}^{left}")))
  km.RemapKey("o", "tab")
  km.Map("*p", _ => (Send("^{tab}")))
  km.RemapKey("q", "appskey")
  km.Map("*s", _ => HoldDownModifierKey("LShift"))
  km.Map("*u", _ => (Send("{blind}+{tab}")))
  km.Map("*w", _ => (Send("^{backspace}")))
  km.Map("*y", _ => (Send("^+{tab}")))
  km.Map("*space", _ => (Send("{blind}{enter}")))

  ; Custom Hotkeys
  km1 := KeymapManager.NewKeymap("customHotkeys", "Custom Hotkeys", "", "")
  km := km1
  km.Map("!'", _ => MyKeymapReload(), , , , "S")
  km.Map("!+'", _ => MyKeymapToggleSuspend(), , , , "S")
  km.Map("!f17", _ => MyKeymapReload(), , , , "S")
  km.Map("#+F23", _ => ToggleCapslock())

  ; ===== 选中动作方案 =====
  ActionSchemeList := Array(
  )
  InitActionScheme(ActionSchemeList)


  KeymapManager.GlobalKeymap.Enable()
}

ExecCapslockAbbr(command) {
  CommandResolver.Resolve("capslock", command)
}

ExecSemicolonAbbr(command) {
}

InitTrayMenu() {
  A_TrayMenu.Delete()
  A_TrayMenu.Add(Translation().menu_pause, TrayMenuHandler)
  A_TrayMenu.Add(Translation().menu_exit, TrayMenuHandler)
  A_TrayMenu.Add(Translation().menu_reload, TrayMenuHandler)
  A_TrayMenu.Add(Translation().menu_settings, TrayMenuHandler)
  A_TrayMenu.Add(Translation().menu_window_spy, TrayMenuHandler)
  A_TrayMenu.Default := Translation().menu_pause
  A_TrayMenu.ClickCount := 1

  A_IconTip := "MyKeymap 2.0-beta33 created by 咸鱼阿康"
  TraySetIcon("./bin/icons/logo.ico", , true)
}


#HotIf