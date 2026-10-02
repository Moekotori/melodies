# 外观设置迁移

日期：2026-10-02。入口：设置 → 外观。旧 D:/ECHO 工作区仅用于只读对照，不读取或写入其生产设置、曲库或账号信息。

## 旧版入口与本次落点

对照 `src/renderer/pages/settings/appearance/AppearanceSettingsSection.tsx`、`AppearanceSimpleSettings.tsx`、`AppWallpaperSettings.tsx`、`TypographySettings.tsx`、`NavigationCustomizationSettings.tsx`，并跟到 `appearancePreferences.ts`、`rainbow-mode.css`、播放器及首页的实际消费者。

| 旧版功能 | 新版状态与消费者 |
| --- | --- |
| 浅 / 深 / 系统 / 氛围 / 亚克力、定时切换 | 复用 ThemeViewModel / WindowThemeBinding；平台模糊不可用时使用实色 |
| 49 个预设、26 项颜色、材质 / 动效、浅深色独立编辑、随机 / 重置 / 撤销 | 复用既有原生主题编辑器和语义资源 |
| 自定义主题保存 / 改名 / 删除 / 导入导出 | 保留原版配色 JSON v2 与 preset v1 兼容；主题库与最近 16 次撤销不另建 writer |
| 反色 | 修正为整个主窗像素的 RGB 反转，包括图片；不再冒充深浅色切换 |
| 彩虹模式 | 八种原生风格，25–400% 速度、20–200% 强度；背景叠层与强调色有真实消费者 |
| 左侧栏布局 | 现有侧栏排序 / 显示编辑入口接到设置，恢复默认恢复实际布局 |
| 自定义图片 / 视频背景 | 主窗按需读取、静音循环、启用 / 移除 / 替换 / 重读、独立竖屏素材和位置、五种适配 |
| 背景高级效果 | 11 个效果预设、缩放 / 亮度 / 模糊 / 不透明度、界面不透明度、统一透明度、可读性保护、重置位置 |
| 视频暂停策略 | 智能策略在失焦时暂停；其余策略在可见窗口继续。所有模式隐藏 / 最小化停止解码；减少动态效果 / 关闭动画保持静态 |
| 主 / 中文 / 备用字体 | 系统字体列表、直接输入、TTF / OTF / TTC 导入、重启恢复和回到系统字体；文件后台读取并注册真实字体集合 |
| 页面文字颜色 | 独立浅深色覆盖、非法颜色拒绝、跟随主题重置；投影到 EchoText |
| 专辑封面形状 | 共用 EchoCoverRadius 资源，接入专辑 / 主播放栏 / 首页 / 歌曲及队列封面 |
| 艺术家封面 | 接到已有头像 / 专辑封面 fallback 缓存；外观页与艺术家页开关同步 |
| 底栏上沿波形 / 高能进度条 | 当前本地曲目实际解码的峰值 / RMS，普通进度条回退；图形不伪造分析结果 |
| 波形滚动条 | 原生指针 / 键盘控制实际 ListBox ScrollViewer；显示装饰性滚动包络，与旧版一致，不称为音频测量 |
| 主页波形图 / 频谱分析 | 使用已有 audio.levelMeter 的 visualSpectrum；按可见首页启停、旧帧失效，缺失时显示等待信号 |
| 禁用所有动画 | 既有 MotionPolicy 继续统一控制，并作用于新增彩虹及背景视频 |

八种彩虹为原生绘制适配，49 个主题为配色适配。旧 CSS 的所有皮肤装饰、HTML/JS/Workshop 插件运行时、Workshop 自带可视化脚本及其主题选择不是原生配色迁移的完成证明；仍遵循开发文档的独立兼容边界。

## 职责与资源

- Contracts 的 AppearancePreferences / WallpaperFraming / ImportedFont 与 AppearanceMedia 是小快照和真实文件解码替换边界，不引用 Avalonia。
- ThemeAppearanceState 复用 ThemeViewModel 的持久化和撤销；文件注册、视频和波形进程由 Bootstrap / Infrastructure 所有。没有新增数据库写入 owner。
- ThemeFontBinding 单个后台读取工作器，最多三份活跃字体 / 64 MiB 字节；ImportedFontStore 每文件 32 MiB、最多 16 个 / 96 MiB。导入读取不在 UI 线程，窗口释放注销集合。
- ThemeWallpaper 一个工作器和一个最新请求。图片最多 16 MiB / 1600 万像素 / 1920 解码边长；视频最多 8192 原始边长 / 3200 万像素、960 解码边长、15 Hz、一帧及一次 UI 回执。换素材、隐藏或关闭只停止该 owner 的解码子进程，不操作播放 Host。
- WindowAppearanceVisualBinding 分析当前一首本地曲目，1024 个峰值与 RMS 桶；单个最新请求、取消、45 秒分析超时、最长四小时输入，不持有整库。失败保留普通进度条。首页原生频谱最多 128 桶、10 Hz、500 ms 时效，隐藏 / 离开首页停视觉计时。
- 彩虹最多六个渐变层，15 Hz；只以 1 Hz 更新强调色资源。隐藏或减少动态效果停止连续更新，不重建所有资源 / 页面。
- InvertOverlay 只持有一张 1×1 白色位图，Difference 混合覆盖主窗，卸载释放。依据 [Avalonia bitmap blend modes](https://docs.avaloniaui.net/docs/graphics-animation/bitmap-blend-modes)。字体集合依据 [Avalonia FontManager 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/Media/FontManager.cs)。
- Windows 媒体解码复用旧工作区经 manifest 记录的 LGPL shared FFmpeg / FFprobe，只读复制到 native/media/win-x64，随附 LICENSE / README / 来源及哈希清单。通过 ECHO_APPEARANCE_FFMPEG 可指定配套解码器；macOS / Linux 二进制分发和实机验证尚未完成。

## 验证证据

本轮含失败修正、构建和重跑累计 **117.14 秒**，单条不超过 60 秒，未做长期 / 全量 / 打包检查。

- 固定源码副本 Desktop 最后编译通过，零错误、六项既有非默认构造函数 XAML loader 警告。
- 32 项短检查通过：设置消费者、真实原生点击、八种彩虹选择、范围归一化、保存 / 恢复、撤销、浅深色独立文字颜色、实际字体集合注册、封面资源、整窗反色像素、真实音频解码包络、视频解码 / 循环 / 取消、背景回到普通状态、非法原生频谱、波形滚动条实际指针输入和窗口 owner 释放。
- 当前完整工作区第一次编译被 SelectedFileDiscovery / FolderSourceSet 参数不匹配阻断。固定副本只为编译基线适配该扫描文件及 RemotePlayback.CreateTrack → CreateTrackAsync 两处调用；生产扫描 / 网盘文件未为此修改。外观产品源码与 XAML 在副本直接使用，新媒体 / 字体基础设施也一并编译。
- 原生界面预览在 `misc/appearance-migration/previews/`：materials-light / dark、background-fonts-light / dark / narrow、typography-* 与 invert-proof。部分名为 typography 的文件是完整外观页滚动位置截图，不能把它们当作字体面板全部控件可见的证明。
- 临时曲库、字体、媒体夹具和配置都在 misc/appearance-migration。没有服务器或账号凭据。

这些证据不代表当前运行中的真实应用被替换，也不代表物理设备频谱、文件选择器、系统亚克力、跨平台、Release/AOT 或完整发布通过。新增中文 / 英文静态控件文案已接到现有语言资源，整套旧主题名称 / 动态描述的多语言收口仍单列。

## 回滚

关闭新增开关、移除背景或恢复默认外观均可回到普通路径；主题状态继续使用既有单 writer 与撤销栈。新应用独立设置保留，旧 ECHO 未修改。移除本次外观绑定和 UI 入口不会迁移或改写旧生产数据库，也不会改动原生音频代码。
