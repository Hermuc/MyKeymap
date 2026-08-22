package generators

import (
	"fmt"
	"settings/internal/script/model"
)

// TypeID 9: MyKeymap 自身动作 (暂停/重载/退出/设置/进缩写模式/大写锁定/锁定)
func mykeymapActions9(a model.Action, inAbbrContext bool) string {
	ctx := Cfg.GetHotkeyContext(a)
	callMap := map[int]string{
		1: `MyKeymapToggleSuspend()`,
		2: `MyKeymapReload()`,
		3: `MyKeymapExit()`,
		4: `MyKeymapOpenSettings()`,
		5: `EnterSemicolonAbbr(semiHook, semiHookAbbrWindow)`,
		6: `EnterCapslockAbbr(capsHook)`,
		7: `ToggleCapslock()`,
		8: `km.ToggleLock`,
	}

	call, ok := callMap[a.ValueID]
	if !ok {
		return ""
	}

	if inAbbrContext {
		return call
	}

	if a.ValueID == 1 || a.ValueID == 2 {
		if ctx == "" {
			ctx += ", , , "
		}
		return fmt.Sprintf(`km.Map("%[1]s", _ => %s%s, "S")`, a.Hotkey, call, ctx)
	}
	if a.ValueID == 8 {
		return fmt.Sprintf(`km.Map("%[1]s", %s%s)`, a.Hotkey, call, ctx)
	}

	return fmt.Sprintf(`km.Map("%[1]s", _ => %s%s)`, a.Hotkey, call, ctx)
}
