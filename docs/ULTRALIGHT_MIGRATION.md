# ECHO Ultralight

日期：2026-10-02。

入口：设置 → 通用 → 窗口与启动 → ECHO Ultralight。默认关闭，立即生效；沿用新应用 `preferences.json` 的有界 `Features` 字典保存。独立的“关闭时隐藏到托盘”决定关闭按钮的行为；Ultralight 决定后台是否卸载主界面。

## 旧版来源与适配

只读对照旧 ECHO 的 `src/main/app/UltraLightModeService.ts`、`WindowResidencyController.ts`、`UltraLightTaskbarRestoreWindow.ts`、`src/renderer/pages/settings/general/GeneralUltralightSetting.tsx` 和 `docs/window-residency.md`。

延续“界面是控制端，播放核心留在后台”的思路。Avalonia 没有独立 Chromium Renderer，因此保留原有原生窗口及任务栏入口，卸载可重建的主视觉树；无需另建任务栏代理，也不重启应用、切换音频后端或重新发送队列。新应用当前的本地及直接 HTTP 音源都由同一 Native Audio Host 控制，不存在旧版 System Output 的 Renderer 所有权转换。未来新增界面拥有的音源必须重新审查卸载准入。

| 场景 | 行为 |
| --- | --- |
| 开关关闭 | 最小化/隐藏仍执行现有可见性预算，保留主界面 |
| 开关开启，最小化 | 主视觉树、页请求和图片租约释放；原生窗口保持任务栏入口 |
| 开关开启，隐藏到托盘 | 相同卸载，任务栏入口移除；托盘恢复菜单/图标保留 |
| 恢复 | 原生窗口与播放 owner 不变，重建当前路由、主题及最新播放/队列视图 |
| 后台关闭开关 | 重建主界面，不主动显示隐藏的窗口 |
| 没有托盘后端 | 不执行隐藏到托盘，保留任务栏恢复入口；设置操作报告原因 |
| 模态窗口仍显示 | 不销毁其父窗口的主界面 |
| 退出 | 显式退出绕过托盘停车；原有应用生命周期保存并释放播放和数据库 |

## 职责与资源

- `MainWindowSurface` 只持有原有主界面 XAML；`MainWindow.Residency` 管重建/卸载。卸载同时清除 Content 和 namescope，避免控件被命名引用留住，释放背景图片、布局动效、页面、弹层及闲置封面。只清理无租约的封面，不释放浮动窗口仍使用的图片。
- `WindowResidencyBinding` 是唯一窗口驻留与托盘 owner：至多一个原生图标、一个菜单、一个带 revision 的延迟清理；关闭后移除平台与偏好订阅。Avalonia 12 的图标通过 Application 的 `TrayIcons` 集合装配，避免只构造对象却没有 native implementation。
- `ShellResidency` 直接读写既有 FeaturePreferences，没有第二套设置事实。设置行和托盘都可切换；语言使用稳定资源键。
- 播放会话、原生进程、SMTC、快捷键绑定、听歌记录和用户显式发起的有界任务继续使用原有 owner。没有主界面时不构造底栏队列行；没有浮动消费者时过滤纯 Clock 的 UI 投递。迷你播放器/桌面歌词保留独立生命周期。
- 恢复异常丢弃不完整的新树，显示原生重试/退出入口；不以重新播放或删除配置进行恢复。图片解码仍在现有后台 worker 中进行。

## 短验证及限制

定向入口：`tests/Echo.Ultralight.UiSmoke`。使用实际 Avalonia Headless 控件、真实 JSON 设置存储和模拟 native 协议；检查开关默认值、最小化/隐藏、namescope 释放、旧树弱引用回收、音频调用不变、相同 generation、EOF 后台推进、后台暂停、恢复当前设置页、浮动消费者时钟、快速往返及显式退出。

短测通过，原生 Avalonia 渲染的明暗设置截图位于 `misc/ultralight/preview/`。这证明控件与会话边界，不代表实机声音、任务栏/托盘物理点击或三平台验收。原生平台的独立测试启动在 15 秒内未完成，已仅停止本轮测试进程树；没有把它作为通过证据。没有执行全量测试或打包。

本轮构建、失败重跑与检查累计约 95 秒。初期桌面模块在将并行歌单的 MVVMTK0034 临时降为 warning 的检查命令中编译通过；独立测试工程 0 warning / 0 error，`smoke3.out.log` 记录通过。之后新增的焦点收口和恢复失败入口通过 C# 编译阶段，但最新完整构建未全绿：普通输出被正在运行的 ECHO 锁定，隔离输出又遇到共享 `obj/Debug/net10.0/Echo.Desktop.pdb` 文件锁（`final-isolated.out.log`）。并行测试工具/历史/歌词模块期间的编译阻断也保留在相应日志中，没有修改它们或停止其他应用来解除锁定。最终源码的完整构建及再次短测仍待无锁环境确认。

没有测量 private bytes、GPU 或旧版完整行为对照，不声称具体内存降幅。Avalonia 全局渲染基础设施仍属于应用；本次释放的是主界面视觉资源，不是卸载框架或 GPU 进程。

回滚：关闭 ECHO Ultralight 即恢复常规驻留；托盘隐藏可独立关闭。不改变旧 ECHO 工作区和旧生产库。

## 2026-10-19 收尾复核

- 修复窗口焦点作用域对旧控件的记忆引用，并释放隐藏后停止更新的渲染器所保留的已脱离视觉树及旧合成子节点。`AvaloniaSurfaceRelease` 集中适配 Avalonia 12.1.3，使用属性注册元数据和 .NET 10 `UnsafeAccessor`，没有反射扫描、后台定时清理或重新启用隐藏窗口渲染。升级 Avalonia 时须重跑本短测核对内部成员。
- `WindowResidencyBinding` 统一创建和释放托盘图标；macOS 系统集成借用同一图标提供原有菜单，合并保留 Ultralight 和隐藏操作。开关切换及平台菜单卸载不会创建第二个图标。
- 短测显式进入“通用 → 窗口与启动”，将聚焦操作放在独立辅助方法中，避免 Debug 临时局部变量干扰弱引用检查；恢复后打开队列抽屉再验证按需列表。
- 当前源码运行 `dotnet run --project tests/Echo.Ultralight.UiSmoke` 通过：最小化及已绘制后隐藏的旧树回收、共享托盘复用和退出释放、同一音频 owner、后台 EOF/暂停、页面/队列恢复、浮窗时钟、设置保存及快速往返。构建只有既有 AVLN3001 警告。
- 另以仅引用该资源释放适配层和 Avalonia Headless 的临时小工程进行 osx-arm64 Native AOT 发布与运行，内部访问器检查通过；这不是整个桌面应用的 AOT 发布验收。未执行全量测试、实机声音/物理托盘验收或内存/CPU 对比，不声称具体降幅。
