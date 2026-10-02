# 原生窗口操作

更新：2026-10-02。

## 入口和职责

- `Shell/MainWindow.axaml` 使用 `BorderOnly` 保留系统可缩放外框，开启客户区扩展，指定 40 DIP 标题区。空装饰模板避免系统绘制第二套标题栏；既有 Melodies 布局和原生圆角偏好保留。
- `Shell/TitleBar.axaml` 标记原生标题拖动区及最小化、最大化、关闭角色；搜索、设置和更多所在区域标记为 `User`。移除手写指针拖动与双击最大化处理。
- 窗口按钮的 `Click` 继续调用现有窗口行为：Avalonia 将原生标题按钮消息转为客户端按钮输入，角色本身不是通用命令绑定。关闭仍经过原有 `Closing`、托盘和退出释放逻辑。
- `Shell/WindowResizeFrame.cs` 提供四边 5 DIP、四角 14 DIP 的透明命中区，位于内容缩放容器之外；四角覆盖边缘区以取得对角角色。最大化、全屏及禁止缩放时隐藏。
- 同一主窗口的歌词舞台也标记原生拖动及窗口按钮角色，不增加另一套窗口 owner。

依据：[Avalonia 装饰说明](https://docs.avaloniaui.net/controls/primitives/windowdrawndecorations)、[12.1.3 角色定义](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/Chrome/WindowDecorationProperties.cs)、[Windows 非客户区输入路由](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Windows/Avalonia.Win32/WindowImpl.CustomCaptionProc.cs)。

## 资源和生命周期

缩放层只有一个 Grid 和八个 Border，无后台任务、缓存、定时器或 I/O。只订阅所属窗口的状态与缩放能力变更，脱离视觉树时解除订阅。缩放由平台执行，不另写窗口尺寸调度。随主界面驻留层卸载和重建，不改变播放器、曲库与数据库 owner。

## 本轮验证

所有构建、失败重跑和短测在 120 秒预算内，单项低于 60 秒。日志与固定源码副本在 `misc/native-window/`。

- 可编译 Desktop 副本接入本轮窗口与顶栏源码、缩放层及歌词角色改动，定向编译通过。
- 真实 Windows 11 HWND 的 `WM_NCHITTEST` 确认标题拖动、三种窗口按钮、搜索/更多客户端输入和八个缩放方向。
- 合成非客户区双击和按钮消息确认最大化/还原只执行一次；最小化后恢复；关闭经过 `Closing` 一次并能取消。
- 最大化、全屏、固定窗口尺寸时缩放层关闭；恢复普通模式后重新可用。
- Headless 原生 Avalonia 明暗渲染及搜索/更多点击通过，预览在 `misc/native-window/screenshots/`。

验证边界：最新完整 Desktop 在本轮已有电台和 WebDAV 依赖引用不一致处阻塞；副本保留先前侧栏、队列和歌词展示实现，仅迁入本轮角色与缩放改动。尚未验收物理鼠标拖动、多屏 DPI、Windows 分屏悬停菜单、macOS/Linux。合成消息及 Headless 截图不等于这些实机行为。

回滚：恢复主窗口装饰设置及原顶栏/歌词拖动处理，移除缩放层调用和文件；不改变持久化格式。
