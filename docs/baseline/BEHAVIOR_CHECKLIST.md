# MyKeymap 热键行为核对清单(阶段 0 基线)

> 本清单是重构期间"零行为变更"约束的验收依据。
> 每完成一个重构阶段,须逐项核对后方可进入下一阶段。

## 基线声明

| 项 | 值 |
|---|---|
| 基线脚本 | `docs/baseline/MyKeymap.ahk.snapshot`(= 重构开始时 `bin/MyKeymap.ahk`,391 行) |
| 配置真源 | `data/config.json`(源码仓库版,16 个 keymap,247 个动作) |
| 生成分支 | `refactor/modularize` |
| 已知漂移 | 部署目录 `MyKeymap-2.0-beta33\data\config.json` 落后于仓库配置,重构期间一律以仓库配置为准 |
| 已知手工行 | `km.Map("!f17", _ => MyKeymapReload(), , , , "S")`:因 `preprocess()` 仅在 keymaps 首位为 id=1 时才注入免疫热键,当前配置首位为 id=5,该行由历史手工保留,**重构必须保持该行为**(阶段 3 修复注入逻辑) |

## 全局设置(生成脚本头部)

- [ ] `SendMode "Event"`、`SetKeyDelay 0`、`SetMouseDelay 0`、`SetWinDelay 0`
- [ ] `A_MaxHotkeysPerInterval := 256`、`ProcessSetPriority "High"`、`#UseHook true`
- [ ] DPI 感知 DllCall、`SetWorkingDir("../")`
- [ ] 启动即运行 `bin\MyKeymap-CommandInput.exe`
- [ ] 托盘菜单 5 项:暂停/退出/重载/设置/WindowSpy,单击=暂停,图标 `bin/icons/logo.ico`
- [ ] 退出时 `MyKeymapExit` 关闭 CommandInput 进程

## 模式 1 — CapsLock(`*CapsLock`,游戏窗口组禁用)

禁用条件:`ahk_group MY_WINDOW_GROUP__1`(Stardew Valley + 牧场物语符文工房3)

窗口操作:
- [ ] `c` 音量控制 / `z` 复制选中为纯文本 / `.` 窗口拖拽 / `a` 居中 1370x930
- [ ] `b` 最小化 / `e` 任务切换(进 TaskSwitchKeymap:e/d/s/f/c/space)/ `g` 置顶切换
- [ ] `p` 下一虚拟桌面 / `y` 上一虚拟桌面 / `q` 最大化 / `r` 循环相关窗口
- [ ] `s` 居中 1200x800 / `d` 居中 1740x1000 / `t` 绑定窗口 / `v` 移到下一显示器
- [ ] `w` 上一窗口 / `x` 智能关闭

鼠标(快慢双速注册):
- [ ] `ijlk` 上下左右移动 / `nm` 左/右键 / `,` 左键按下 / `uo` 滚轮上下 / `h;` 滚轮左右
- [ ] `/` 鼠标移到光标处

特殊:
- [ ] `0` 清行并输入固定文本段落(含 Sleep 1000 + 回车确认)
- [ ] 单击(450ms 内松开)→ 进入 CapsLock 缩写输入(`EnterCapslockAbbr`)

## 模式 2 — CapsLock+F(16 个程序唤起)

- [ ] `a` 终端预览 / `d` Edge / `e` 资源管理器 / `h` VS2019 / `i` Typora / `j` IDEA
- [ ] `k` PotPlayer / `l` Excel / `n` GoLand / `o` OneNote / `p` PowerPoint
- [ ] `q` Everything / `r` FoxitReader(绝对路径)/ `s` VSCode / `w` Chrome
- [ ] `m` QQ(存在则发 `^!z`,否则启动)
- [ ] 单击 → 发送 `{blind}{f}`

## 模式 3 — CapsLock+Space

- [ ] `d` DataGrip / `w` 微信(存在则发 `^!w`)/ 单击 → 发送 `{blind}{space}`

## 模式 4 — J 模式(`*j`,Vim 风格方向键)

- [ ] 重映射:`a`=home `g`=end `s`=left `f`=right `e`=up `d`=down `c`=backspace
  `x`=esc `r`=tab `q`=appskey `,`=delete `.`=insert
- [ ] `z/v` 按词左右(`^{left}`/`^{right}`)/ `b` 删词 / `t` 清行
- [ ] `2/3` 切换标签页(`^+{tab}`/`^{tab}`)/ `w` `+{tab}` / `k` 按住 LShift
- [ ] `i` 输入 "ji" / `space` 回车 / 单击 → 发送 `{blind}{j}`

## 模式 5 — 3 模式(功能键 + 数字键盘,支持锁定)

- [ ] 数字重映射:`j`=1 `k`=2 `l`=3 `u`=4 `i`=5 `o`=6 `b`=7 `n`=8 `m`=9 `h`=0
- [ ] 功能键:`space`=F1 `2`=F2 `4`=F4 `5`=F5 `9`=F9 `0`=F10 `e`=F11 `r`=F12
- [ ] `t/w` 音量加减 / `/` 锁定切换(`km.ToggleLock`)/ 单击 → 发送 `{blind}{3}`

## 模式 6 — 分号模式(符号输入)

- [ ] `a`=`*` `b`=`%` `c`=`.` `d`=`=` `e`=`^` `f`=`>` `g`=`!` `h`=`+` `i`=`:`
  `j`=`;` `k`=(反引号) `m`=`-` `n`=`/` `r`=`&` `s`=`<` `t`=`~` `u`=`$`
  `v`=`|` `w`=`#` `x`=`_` `y`=`@` `z`=`"`(均 `{blind}`)
- [ ] 单击 → 进入分号缩写输入(`EnterSemicolonAbbr`,带 InputTipWindow)

## 模式 7 — 句号模式(方向键变体)

- [ ] 重映射与 J 模式同构(`a/g/s/f/e/d/c/x/r/q`),无 `,/.` 两项、无 `k`
- [ ] `,` 按住 LShift / `2/3` 切标签 / `z/v/b/t/w/space` 同 J 模式
- [ ] 单击 → 发送 `{blind}{.}`

## 模式 8 — 鼠标右键

- [ ] `XButton1/2` 下/上一虚拟桌面 / `LButton` 任务切换 / `MButton` `#{tab}`
- [ ] `c`=backspace `d`=delete `x`=esc / `space` 回车
- [ ] 滚轮上/下 = 切换标签页 / 单击 → 右键(快慢双速)

## 模式 9 — Custom Hotkeys

- [ ] `!'` 重载(免疫挂起)/ `!+'` 暂停切换(免疫挂起)/ `!f17` 重载(手工保留行)
- [ ] `!CapsLock` 切换大写锁定状态
- [ ] 窗口组 1 = chrome+msedge+firefox;游戏组 = -1(禁用全局)

## CapsLock 缩写(28 条,`ExecCapslockAbbr` switch)

- [ ] 启动类:ca 计算器 / no 记事本 / cmd 命令行 / dd 下载目录 / dm 工作目录
  lj 回收站 / ga Game 目录 / sp Spotify 网页 / ss Spotify 客户端 / we 网易云
- [ ] 选中相关:{selected} 占位 — bb Bing 词典(带 --app)/ cc VSCode 打开选中文件
  gg Google 搜索 / wt 终端打开选中路径
- [ ] 系统类:gj 关机 / rb 重启 / sl 睡眠 / rex 重启资源管理器 / ld 亮度
  mu 静音应用 / kp 关进程 / pd 进程所在目录 / vm 应用音量设置 / ly 蓝牙设置
  wf 网络面板 / tm 任务管理器
- [ ] MyKeymap 类:se 打开设置 / ex 退出
- [ ] 项目类:mm MyKeymap2 / ms my_site(VSCode 打开目录)

## 分号缩写(17 条,`ExecSemicolonAbbr` switch)

- [ ] 中文标点:`,`→，`.`→。`/`→、
- [ ] 代码块:dk `{}` / xk `()` / xf `();` / sk 「 」/ zk `[]`
- [ ] 文本:gt 🐶 / jt ➤ / i love nia 固定文本 / kg 中英文加空格
- [ ] 日期:rq 日期 / sj 日期时间
- [ ] 脚本/模板:gg git 三连模板(光标回退 47)/ zh 知乎站内搜索后缀
  dq AlignComment.ahk / fz CollectText.ahk(后台/非后台差异须保持)

## 选中动作方案

- [ ] 当前 `ActionSchemeList` 为空数组,`InitActionScheme` 正常执行不注册任何热键
  (重构后此路径须保持可用,为阶段 2 拆分做铺垫)

## 核对方法

1. 阶段 1(纯搬家):生成脚本与基线**字节级一致**(忽略 `#Include` 路径行)
2. 阶段 2+:与上一阶段产物做行为 diff,逐项人工验证本清单中标注受影响的条目
3. 关键交互(模式进栈/出栈、单击检测、锁定、缩写输入)须在真实键盘上验证
