# 主界面底栏迁移

日期：2026-10-02。对照旧工作区 `src/renderer/components/player/PlayerBar.tsx`、`PlayerTransport.tsx`、`PlayerControlIcon.tsx` 与最终加载的 `player-bar-polish.css` / `player-transport-polish.css`。旧工作区只读。

## 默认波形与信号图标修正

- 默认进度使用旧版 `PlayerProgress.tsx` 的按歌曲/时长播种的波形，按宽度使用 96/64/48 根柱；不足 142px 时进一步减小数量，确保 150% UI 缩放下柱子不溢出、颜色比例不偏移。已播放柱使用主题强调色，未播放柱使用淡色。点击和拖动按整个波形宽度计算位置，键盘保留原生 Slider 语义。它是旧版的生成式进度外观，不是音频采样。
- `PlayerWaveform` 最多缓存 96 个高度，播放位置变化只绘制颜色，不重复生成高度、分配种子字符串或启动计时器。`PlayerWaveformSlider` 处理全区域指针捕获；失焦与脱离释放捕获，提交/取消仍由既有 `PlayerSliderInteraction` 负责。
- 信号图标迁入当前旧版 `SignalPathStateIcon.tsx` 的四角星及卫星点，沿用对应 CSS 的明暗语义颜色。未确认设备显示空心断开态；已确认输出显示普通状态；DSP 与 bit-perfect 候选只使用明确报告的字段，不能从请求采样率推断。新端尚无网络输出，金色外观仅保留在控件的状态集合。
- 独立原生控件工程 `misc/player-waveform-check` 直接编译产品的三个控件和共用 XAML 样式，已通过指针比例、键盘、窄宽度、明暗及五种图标外观检查。整页检查曾被并行的主题/快捷键/侧栏搜索修改挡住；最后一次整仓构建被 `LibraryDatabase` 尚未实现的 6 项歌单写接口阻塞。不能把独立控件检查称为当前整仓通过。此修正整轮构建/短检查累计约 25 秒，未跑音频长测或打包。

来源另外包括 `src/renderer/styles/signal-path-state-icon.css`。原生控件图在 `misc/player-waveform-check/render`。

## 当前范围

- 保留主窗口 108px 底栏、宽窗口 84px 封面、三列布局、旧版双色播放图标及明暗语义资源；窄窗口与 150% UI 缩放压缩封面和可选按钮，保持进度与主要播放控制可用。
- 封面点击进入现有歌词页，真实图片通过已有 `QueueArtwork` / `AlbumArtworkCache` 加载；缺图保留原版吉祥物。
- 左侧显示当前曲名、艺术家、真实播放状态与音源格式；更多菜单显示专辑、位深和采样率。音源参数与信号路径中的实际设备输出分开显示。
- 曲库内歌曲可以喜欢/取消喜欢；以文件身份索引读取一条记录，通过现有唯一 writer 保存，切歌后重新读取。文件选择打开的曲目如果未入曲库，喜欢按钮禁用。
- 播放/暂停、前后切歌、四种播放顺序、歌词、队列保持调用现有播放会话。信号路径、歌曲菜单、音量与输出设备设置分开呈现。
- 进度/音量拖动只持有本地预览，松手提交一次；键盘支持方向键、Home/End、PageUp/PageDown。失焦、捕获丢失、控件脱离取消预览；跨曲目或输出 operation 的 seek 不提交。
- 音量弹层增加百分比、静音/恢复与 100% 恢复入口；输出设备面板保留原有 ASIO、DSD 与音效配置能力。

这是一轮基础底栏迁移。歌词页的完整 / 迷你底栏已另行迁入，见 [歌词底栏迁移](LYRICS_PLAYER_BAR_MIGRATION.md)。其余高级能力的状态以对应模块最新迁移说明为准；不以静态装饰波形代表音频分析。

## 职责与生命周期

`PlayerBar` 负责布局、文件选择、导航与弹层。`PlayerVolumePanel` 负责局部音量交互；`PlayerOutputPanel` 承接既有设备配置。`PlayerSliderInteraction` 管理一次输入预览与提交，播放事实仍由 `PlaybackSessionCoordinator` 持有。

`PlayerPopoverBinding` 为离开窗口资源树的弹层提供局部主题作用域与本功能的语言资源，仅语言/主题改变时更新；脱离控件时撤销监听。弹层打开期间切换深色主题已检查，不依赖预览工具的补资源路径。

`PlayerTrackDetails` 每次只持有当前一个队列 DTO / 曲库 DTO，快速切歌取消旧读取，迟到结果不更新新曲目；`Detach` 取消读取并清空引用。`IPlayerTrackLibrary` 是活跃歌曲的读写端口，基础设施沿用现有 interactive reader 和唯一 writer，不建立第二连接所有者，不访问旧生产库。

封面每个可见控件最多一个图片租约，沿用单工作器、64 个请求、32 MiB 缓存与 320px 缩略图上限；脱离视觉树取消请求并归还租约。不增加播放定时器、整库常驻数据或原生音频修改。

## 短验证

`tools/Echo.Preview --playerbar misc/playerbar-migration/render` 使用原生 Avalonia 控件、隔离的新格式 SQLite 与明确模拟的音频确认：检查喜欢持久化、单曲索引读取、实际图片加载、鼠标拖动只提交一次、键盘 seek / volume、切歌资料替换、封面导航、1440/1180/980/800 宽度及 150% UI 缩放，并保存明暗主题和弹层 PNG。

构建与检查每条不超过 60 秒，整轮累计不超过 120 秒。模拟音频确认与 Headless 画面不能证明实机输出、真实鼠标手感、长期资源或跨平台一致性；真实设备使用既有 `tests/Echo.Playback.UiSmoke` 单独短检查。

最终当前源码构建通过，保留歌词页与两个设置抽屉的 3 条既有 runtime loader 警告。最终底栏检查约 1.7 秒；另有约 2.3 秒真实 Windows Native Audio Host 静音 smoke，确认暂停、seek、两个控制端的断开/重连、窗口与播放核心生命周期及 checkpoint。此检查不代表可听音质验收。所有构建/检查及失败重跑累计约 64 秒，未运行打包或长测试。

并行迁移期间曾出现扫描/艺术家/专辑未收口的编译状态与预览语言缺字，中间使用的成功构建快照保存在 `render`；最终基于当前工作区重新通过检查，交付图在 `misc/playerbar-migration/render-current`。最终快照与模拟音频预览不能替代真实设备和整页视觉验收。

旧实现保留，可停止新应用回到旧 ECHO；本变更不修改数据库 schema，喜欢写入仅发生在新格式曲库。
