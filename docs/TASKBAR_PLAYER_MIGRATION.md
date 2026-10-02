# Windows 任务栏播放器

2026-10-02。按用户截图接入原生任务栏缩略图播放器，默认启用。入口：设置 → 连接与服务 → Windows 媒体与任务栏。普通浮动迷你窗口独立保留。

封面尺寸修订：默认仅封面模式填满 256px 预览画布，删除内部空白；带信息模式从 184px 放大到 208px，保留文字空间。修订源码快照编译和仅视觉预览约 9 秒；最新素材示意在 `misc/taskbar/larger-cover/taskbar-preview.png`，未追加播放测试或实机悬停验收。

## 已接入

- 原生任务栏歌曲标题、256px 封面预览、无封面占位；高级设置切换仅封面 / 封面与歌曲信息。
- 上一首、播放 / 暂停、下一首、喜欢 / 取消喜欢、打开播放队列。喜欢显示同一曲库写入确认后的结果；曲库外歌曲禁用喜欢。切歌期间以 occurrence ID 校验资料身份。
- 任务栏播放进度：准备中、播放中、暂停、失败；独立开关与刷新 / 重试入口。播放失败时播放按钮走已有恢复命令。
- 与同一播放 owner、队列和收藏服务连接。Ultralight 卸载主视觉树后仍控制播放；队列按钮恢复主窗及队列抽屉。功能关闭隐藏五个原生按钮并恢复普通窗口预览。
- 中文 / 英文选项与提示；标题、播放 / 暂停和喜欢图标更新；Explorer 的 TaskbarButtonCreated 事件重新注册工具栏。

## 职责与资源

`WindowTaskbarBinding` 在 Bootstrap 装配和释放，订阅有界最新帧，不依赖驻留页面。只保留一帧播放状态；最多一个 UI 投递、一个可取消封面请求；进度最多每秒一次。关闭窗口前解除 Win32 hook、取消读取和请求、释放原生资源。

`TaskbarArtworkRenderer` 只做小型内存栅格化。封面从现有 32 MiB ArtworkCache 获取 ≤320px 租约，DWM 请求时才加载，复制完立即归还；切歌取消旧请求并拒绝迟到结果。原生适配持有七枚 32px HICON 与一份 256px BGRA 封面，关闭功能释放封面；DWM 每次请求暂建 ≤512px HBITMAP，调用后立即删除。没有 UI 文件 I/O、整库持有、轮询截屏或第二个写入 owner。

`Echo.Platform/Windows` 隔离 Win32、GDI、DWM 和 ITaskbarList3。COM 使用显式 ABI 和配对 apartment 生命周期，避免依赖动态 COM 包装；本轮未做完整 AOT 发布。外框、标题栏字体和按钮布局由 Windows 管理。产品像素使用中性浅深色、紫色收藏与双色窄描边，确保应用和任务栏主题不一致时仍可辨认。

## 验证与边界

入口 `tools/Echo.TaskbarPreview`：实际产品命令、模拟音频 / 曲库、Avalonia Headless 的最小化 / 卸载 / 队列恢复、关闭与不可收藏限制、语言切换；Win32 HWND、COM、HICON、DWM 多尺寸请求和重复资源释放单独短检。图片 `misc/taskbar/taskbar-preview.png` 使用产品封面栅格与按钮资源，系统外框为示意，不是 Explorer 截图。

首轮共享工程受并行电台剪贴板代码阻塞；随后受并行 `LibraryPopupStyles.axaml` 的 ToggleSwitch 必需模板部件缺失阻塞。定向验证采用当前源码快照，仅在 `misc/taskbar/snapshot` 中补齐该部件；没有修改共享业务文件。隔离源码构建通过；已有五个 AVLN3001 运行时 XAML 加载提示保留。最终完整应用构建以最新共享工作区日志为准。

最终真实 Avalonia HWND 检查通过：ITaskbarList3 接受五个按钮，实际 Win32 hook 分派暂停 / 恢复、前后切歌、喜欢和队列恢复；DWM 接受 32px 缩略图与 256px live preview。此前人工发送 256px 缩略图请求被 DWM 拒绝，说明人工消息不改变系统已有尺寸限制；产品收到真实 DWM 请求后取消息中的最大宽高并缩放。资源夹具使用不进入任务栏的隐藏 STATIC HWND，其 ThumbBar 的 `0x80070057` 作为诊断保留，不能代替实际窗口结果。

短检证明控制逻辑与资源边界；音频 / 曲库为夹具，真实扬声器、数据库持久化和 Explorer 鼠标悬停 / 点击另行验收。资源计量排除一次 Windows / COM 首次初始化，再比较三个创建 / 释放周期：GDI、USER 增量均为 0；不作为长期无泄漏或性能结论。本轮全部构建、失败、修补重跑与短检累计约 108 秒，每条均少于 60 秒；只停止自己启动的验证进程。

微软接口依据：[任务栏工具栏](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist3-thumbbaraddbuttons)、[DWM 封面缩略图](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmseticonicthumbnail)。

## 回滚

关闭“任务栏迷你播放器”和“显示任务栏播放进度”即可恢复普通预览与无进度任务栏。代码回滚只撤销 DesktopComposition 的任务栏装配、本模块和设置目录新增项，不改播放核心、音频设备、扫描或数据库。
