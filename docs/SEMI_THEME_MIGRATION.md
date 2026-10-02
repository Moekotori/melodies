# Semi 控件主题接入

更新：2026-10-02。

桌面基础控件改用 [Semi.Avalonia 12.1.0.1](https://www.nuget.org/packages/Semi.Avalonia/12.1.0.1)，继续使用锁定的 Avalonia 12.1.3。Desktop 不再引用或装配 FluentTheme；中央版本表中的 Fluent 包暂由已有独立队列预览工具使用。

## 职责与边界

- `App.axaml` 只装配一套 SemiTheme。普通按钮、输入框、复选框、开关、基础滑条及下拉框使用 Semi 模板。
- `EchoControls.axaml` 的轻量模板仅用于 ECHO 导航、窗口、播放和内容操作；主按钮使用 Semi SolidButton。保留既有页面结构、播放栏与歌词舞台。
- `EchoSelectors.axaml` 的两个命名主题继承 Semi 原生主题，继续供紧凑曲库筛选调用，保留最多 340 DIP 的下拉高度与应用内弹层。移除全局手写下拉模板。
- `SemiResourcePalette` 在既有窗口主题 owner 创建配色时同步固定数量的 Semi 控件资源。输入是当前主题颜色；输出是窗口明暗资源字典。无 I/O、缓存、订阅或控件引用；随主题替换及窗口释放，不新增调度或数据库 owner。
- `WindowLocalizationBinding` 同步 Semi 控件语言。语言资源进入现有可替换字典，不共享可变窗口资源；Semi 未支持的语言使用英文控件文本，ECHO 页面仍使用自身语言包。
- 默认经典配色调整为白色 / 中性深灰背景、靛蓝强调色、6 DIP 面板圆角及较轻阴影。已有自定义主题及用户覆盖仍由原主题系统负责。

不改变曲库、扫描、数据库、音频与旧工作区；不读取用户数据库或保存用户配置进行预览。

## 短验证

本轮全部构建、失败修正与预览重跑累计约 79 秒，单条不超过 60 秒；产物隔离在 `misc/semi-theme/build/`。

- Desktop 与独立原生预览工具最终编译通过。Desktop 有 6 条 AVLN3001 无公共构造函数资源加载警告，未执行 AOT 发布。
- `tools/Echo.ThemePreview` 使用真正的 App、MainWindow 和 ShellViewModel，Headless + Skia 渲染实际 Avalonia 控件；不使用 HTML 重画。
- 主界面 / 设置的明暗、英文、900px 窗口截图完成。
- 实际鼠标输入检查普通按钮执行、禁用后不执行；下拉打开、键盘选择及关闭通过。普通按钮确认未采用 ECHO 手写模板。
- 查看最终首页、设置、控件及下拉截图后修正开关强调色与禁用文字过淡。

截图：`misc/semi-theme/previews/`。这是隔离的原生控件预览，不代表运行中应用已重启、真实播放或三平台验收；未测长期性能。

复现：构建 `tools/Echo.ThemePreview/Echo.ThemePreview.csproj`，执行其输出 DLL 并传入截图目录。回滚按接入文件恢复 Fluent 装配与依赖、控件覆盖及经典配色；保留其他并行功能修改，不重置整个工作区。
