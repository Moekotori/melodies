# Wallpaper Engine 背景

Control 端负责本机外观素材及画面呈现。`Echo.Wallpapers` 是独立的动态壁纸兼容模块，
不引用 Desktop、Core 或播放实现，不打开曲库数据库，也不持有音乐播放设备。

自定义背景和竖屏背景均支持图片、静音视频、Wallpaper Engine 场景和网页项目。
选择器发现 Steam 多曲库中的 Workshop 项目和本地编辑器项目，也可手动选择目录。
导入复制到本机外观目录，原始项目只读。场景包使用 PKGV 索引和范围流读取，
材质、纹理、脚本、粒子、文字、3D 和网页属性桥复用 Electron ECHO 的运行时。
这是与 ECHO 相同方向的兼容实现，不宣称完整覆盖 Wallpaper Engine 私有引擎。

普通图片和视频不启动浏览器。场景/网页按需启动独立的 Chromium 无界面渲染进程，
优先使用 `wallpaper-runtime` 下的随附运行时，其次查找系统 Edge、Chrome、Chromium。
可用 `ECHO_WALLPAPER_BROWSER` 指定可执行文件；此路径不是浏览器用户配置。
场景依赖的公共材质、着色器及基础脚本只读使用本机 Wallpaper Engine 安装的 assets，
也可用 `ECHO_WALLPAPER_ASSETS` 指定，不复制或随应用分发这些第三方资源。

每个窗口最多一个当前渲染器。画面最长边 960 px，前台最多 15 FPS，失焦继续播放时 5 FPS；
串行请求和 UI 确认形成背压，不排队积累画面。沿用背景的缩放、位置、亮度、模糊、透明度。
智能暂停冻结当前网页及其 iframe，保留画面；恢复时继续同一实例。
窗口隐藏/最小化、切换素材、禁用背景或释放窗口时终止对应进程树及资源服务，清理临时配置目录。
没有无条件重启循环。启动、协议或 WebGL 失败显示真实错误。

本地资源服务只监听 loopback，使用随机会话路径，只提供当前项目及只读 assets。
网页在仅允许脚本的 iframe 沙箱中运行，禁止任意网络连接、顶层导航、窗口、表单、插件和外部文件访问。
支持有界 HTTP 字节范围及最多 8 个资源流；不向网页暴露文件、Core 或播放控制 API。
仅将已确认的播放元数据、时间、可用频谱及所需光标信息送入背景，背景音频静音。

资源上限：项目 5000 个文件/2 GiB，最多 16 个导入项目；包索引 20000 项/8 MiB；
协议单消息 8 MiB；JS 堆上限 192 MiB；场景资源文本缓存 16 MiB/1024 项；
纹理缓存 256 项；纹理与渲染目标预算 192 MiB（保守计入 mip，不代表进程树总占用）。
渲染进程退出统一释放 GPU 和脚本资源。基本 .NET 构建使用已生成的浏览器脚本，不需要 Node。

聚焦验证入口：`tests/Echo.Wallpapers.Smoke`，可加 `--installed` 检查本机 Neon Sunset；
`tests/Echo.Presentation.Smoke --wallpaper-project <导入入口>` 检查实际 Avalonia 画面和资源释放。
Windows Edge 已做真实渲染短测；macOS、Linux、Native AOT 发布及全量 Workshop 兼容性仍需分别验收。
