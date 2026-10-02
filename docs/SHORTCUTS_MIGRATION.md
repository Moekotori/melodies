# 快捷键迁移

日期：2026-10-02。入口：设置 → 快捷键，以及设置搜索结果中的具体动作。

## 兼容与交互

旧版来源为 `D:\ECHO/src/shared/types/globalShortcuts.ts`、`src/main/app/backgroundPlaybackShortcuts.ts`、`src/renderer/components/player/PlaybackCommandController.tsx`，只读对照，不读取旧生产库或自动覆盖旧配置。

保留旧版全部 33 个动作 ID、应用内推荐组合及全局推荐组合。首次启动应用内仅启用 Space 播放/暂停和 Ctrl+Q 队列，全局全部关闭且未绑定。恢复推荐会显示旧推荐的全局组合，仍保持关闭。同期全局搜索模块新增的动作也使用同一绑定系统，不改变旧默认值。

每项可独立录制、启用、清除；录制成功立即生效并异步保存。Esc、切换类别、离开页面或主窗口失焦取消录制，保留原绑定。录制开始先移除全局注册，确认完成后才接收组合键；录制时不触发播放命令。重复组合会显示冲突动作，保留原设置。同组合跨应用内/全局可存在，以已确认注册的全局入口执行一次。

输入框、数字输入、下拉框、滑块保留自身键盘行为，按钮上的 Space 不额外控制播放器。普通快捷键忽略按住造成的重复触发；全局使用系统 no-repeat 注册。Ctrl+B 侧栏、F11 全屏及 Esc 的结构操作保持独立，录制会提示保留组合。搜索、问题筛选、注册重试、中英文和窄窗口布局均有实际入口。

音量 ±5%、速度 ±0.1、快进退 ±10 秒、单曲循环/关闭切换、静音恢复上一音量。播放事实仍由现有 PlaybackSessionCoordinator 持有；快捷键没有第二个播放 owner。速度与 EQ 只提交各自 DSP 控制，不重新配置输出设备。喜欢使用既有曲库命令；我喜欢的打开歌曲过滤；定位歌曲打开当前队列并定位 occurrence。歌词、音频/歌词抽屉、主窗口及已装配的浮动窗口使用现有入口。

老板键静音，在托盘恢复入口可用时隐藏主窗口，托盘不可用时最小化；用户可从托盘、任务栏或显示主窗口全局绑定恢复。超轻模式释放主窗口视觉树时，界面类快捷键先恢复窗口再分派，后台播放类快捷键不重建视觉树。打开歌曲文件夹使用平台 Launcher 打开目录，尚未实现各系统文件管理器中选中具体文件。

MV 设置、桌宠等尚未迁入的能力保留在清单中并禁用，不把记录绑定当作已实现对应产品功能。锁定桌面歌词根据浮动窗口能力表开放。当前自定义全局后端为 Windows；macOS/X11/Wayland portal 尚待迁移，非 Windows 页面明确标注，并禁用自定义全局绑定。现有系统媒体控制独立管理。

## 职责与资源

| 模块 | owner / 输入输出与边界 |
| --- | --- |
| Contracts/Shortcuts | 规范化组合、范围、绑定 DTO、平台及配置端口，无 Avalonia/数据库依赖；最多 64 动作/128 绑定，组合文本最多 96 字符 |
| Application/Shortcuts | 旧版动作目录与推荐值；设置搜索与运行时共用 ID |
| Desktop/Features/Shortcuts | 窗口内有界行与录制状态；编辑时重建索引，按键查表，无周期性扫描或计时器 |
| MainWindow.Shortcuts | 真实控件输入防护与命令分派；最多一个正在执行的快捷键操作，忙时不积累连按队列 |
| WindowShortcutBinding | 唯一平台装配与生命周期；一个运行中的注册替换、一个合并后的待替换状态；迟到结果按 revision 拒绝；跨线程按键最多一个待处理派发 |
| WindowsGlobalShortcuts | 首次启用全局绑定才创建阻塞消息线程；RegisterHotKey/WM_HOTKEY，无键盘轮询。只在启用鼠标绑定时安装 WH_MOUSE_LL；钩子只读按键状态并投递动作。编辑保留无变化的注册，退出全部解除 |
| JsonShortcutProfileStore | 独立 `ECHO Avalonia/shortcuts.json`，最多 32 KiB。一个后台写入者、容量 1 的合并队列、临时文件原子替换。UI 不执行文件 I/O，退出等待最后保存 |

注册失败保留用户配置并显示未注册原因，用户可改键或重试；不把保存成功冒充系统注册成功。应用内配置与全局注册确认分开。导入错误保留当前默认或当前已编辑状态，不在异步恢复完成时覆盖用户刚做的修改。

Windows API 依据：[RegisterHotKey 与 MOD_NOREPEAT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[LowLevelMouseProc 生命周期与回调约束](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)。

## 验证与回滚

独立定向入口：`tests/Echo.Shortcuts.Smoke`，输出参数为 `misc/shortcuts`；亦保留 `Echo.Preview --shortcuts misc/shortcuts`。内部 20 秒取消预算，外部最多 25 秒。夹具包括真实 Avalonia 控件路由、录制/冲突/取消/重映射/编辑控件/重复派发、全局确认与失败/重试、独立 JSON 保存读取、明暗/窄窗口/英文原生截图，以及 Windows 系统注册、占用/解除、合成 OS 按键派发和鼠标钩子生命周期。

音频使用确认夹具；平台注册另用真实 Win32。按键索引短测仅是当前查表模型，不是旧版完整性能对照。当前工作区有并行功能开发，编译结果及未验收范围见下方交付记录。

物理键盘/鼠标侧键、前后台切换、真实设备播放与长时间资源行为仍需实机验收。macOS、Linux 全局适配不可用。回滚撤销本模块的装配及输入处理、保留独立配置文件，不改变曲库 schema、原生音频、旧应用或旧配置。

交付记录：独立 smoke 在前次成功构建的桌面程序集上通过，耗时约 7.2 秒；当前索引模型 100000 次读取约 1.50 ms，不是完整事件时延或旧版对照。四张原生截图位于 `misc/shortcuts/`。随后修正窄窗口工具栏、重复状态文字、配置边界与恢复幂等，并补充系统特殊键映射；平台和配置程序集已编译，最新整桌面构建未完成：同期 `LibraryPopupStyles.axaml` 的 ToggleSwitch 模板缺少 `PART_MovingKnobs`。运行中的桌面进程锁定默认输出，未结束该进程，曾尝试隔离输出验证。所有构建、失败重跑、短测及超时等待累计约 110 秒，停止本轮验证；最新界面调整、完整程序集与运行中的窗口仍待验收。
