# 歌词界面迁移

日期：2026-10-02。入口：播放栏「歌词」。所有内置样式 ID 保持与旧版注册表一致。

歌词使用独立的窗口内容宿主，覆盖整个客户区；不进入普通页面的侧栏 / 播放栏 / 滚动容器。透明的角落控制与悬浮播放控制叠放在舞台上；Esc / 返回恢复先前页面及窗口状态，F11 或角落按钮切换系统全屏。底栏支持完整 / 迷你、鼠标离开仅进度与播放时自动隐藏；暂停、键盘或鼠标操作恢复，弹层和抽屉打开时保持可操作。详见 [歌词底栏迁移](LYRICS_PLAYER_BAR_MIGRATION.md)。普通页面的 UI 缩放和侧栏偏好不被歌词修改。

## 可运行能力

本地格式与来源的后续增量见 [本地歌词兼容](LYRICS_LOCAL_COMPATIBILITY.md)：QRC / KRC、独立译文 / 罗马音、扩展命名、子目录、语言后缀与旧编码已经接入，完整桌面与 UI 验证边界以该记录为准。

| 样式 ID | 名称 | 原生呈现 |
| --- | --- | --- |
| default | 简约 | 留白、上下文滚动与局部高亮 |
| luminous | 浮光 | 暗色舞台、柔和光束，播放时光束最多 30 fps |
| afterglow | 余音 | 居中单句、原版字素时间与弹簧运动 |
| afterglowCover | 余晖 | 左侧封面、暖色底与右侧阅读列 |
| editorial | 纸笺 | 纸张底色、衬线歌词、左侧封面与歌曲信息 |
| folded | 折光 | 青紫光场与原版密度阅读列 |
| roseVinyl | 留声 | 实际封面、阅读列与克制排版 |
| cinemaStage | 映幕 | 实际封面背景、居中字幕构图 |
| kineticPoster | 跃字 | 大字海报、已唱 / 当前 / 未唱词三栏 |
| coverStage | 画境 | 左侧大封面、右侧歌曲信息与歌词 |
| cutBoard | 切影 | 斜切封面、切片与底部边线；原隐藏样式也可选 |
| jizura | Afterglow | 独立主题场景、当前句与逐词绘制 |

Afterglow 已补齐当前原版 23 个主题：mist、night、dusk、paper、aozora、neon、matsuri、kabuki、yuki、junkissa、hokago、mahou、neko、dotto、echoes、time、brick、psyche、code、math、icpc、block、rhythm。实际接入原版脚本 / 规划器 / 场景，由 Jint 与 Skia 承接原生绘制，替换此前近似方向演示。最新证据及限制见 [JIZURA_NATIVE_ENGINE.md](JIZURA_NATIVE_ENGINE.md)。

样式数量、源码复用与原生绘制检查不等于逐像素 / 动态视觉验收。没有引入浏览器或把旧 Renderer 常驻在新应用内。创意工坊自定义歌词主题、MV、在线匹配与歌词编辑器属于独立能力。

歌词来源：同名本地 LRC、增强 LRC、YRC、TTML、SRT、VTT、TXT，或文件选择器导入。相同时间戳的 LRC 次行显示为译文；TTML 读取翻译、罗马音角色与真实词时间。没有真实词时间时整句着色，不伪造逐字时间。导入选项在当前窗口、当前曲目内跨页面保留，不写音源目录，不建立全库文件映射。

显示设置包含主字号、次字号、字体、对齐、上下文、译文、罗马音、逐字高亮、时间偏移与 Afterglow 主题。独立 JSON 文件保存于新应用的 `ECHO Avalonia/lyrics-settings.json`，不触碰旧生产库。字体列表使用本机字体与后备字体，不下载字体。正偏移让歌词提前；跳转同时扣除文件 offset 与显示 offset。

滚轮、方向键、PageUp/PageDown 和右侧滚动条浏览歌词；回车或点击有时间的行跳转播放；Home 或「回到当前歌词」恢复跟随。空歌词、加载、纯文本与格式错误都有明确状态及重新读取入口。

## 职责与资源所有权

- `Echo.Contracts/Lyrics`：纯文档、词时间、读取与设置端口；二分定位当前行。
- `Echo.Application/Lyrics`：纯解析及标准化；后台调用，支持取消。
- `Echo.Infrastructure/Lyrics`：一个读取准入器，最多 1 MiB 文件 / 512 KiB 文本；设置使用容量 1 的最新值队列和单写入者。
- `LyricsViewModel`：只持有当前文档、一次可取消加载、合并后的最新播放快照和一个共享封面租约。退出、换曲或新请求使旧结果失效。
- 独立歌词舞台通过原有封面 owner 申请最长边 768 像素的租约，按分辨率隔离缓存键；普通曲库缩略图仍为 320。两者共用固定工作器及 32 MiB 硬预算，不另建封面读取队列或缓存 owner。
- `LyricsAppearance` / `LyricsSelection`：窗口所属的显示偏好及两个路径组成的当前导入选择，无全文或全库缓存。
- `LyricsViewportControl`：当前行附近绘制、点击命中、浏览与字体排版缓存；无逐词控件。排版缓存最多 18 项，估算上限 4 MiB，字体/宽度/DPI/曲目变化失效。
- `LyricsBackdropControl`：独立保留背景层；词帧不重建封面或场景。`LyricsStagePainter` 与 `AfterglowScenePainter` 分别维护普通样式和 Afterglow 意象。
- `MainWindow.Lyrics`：独立内容宿主与正常页面恢复。`LyricsView.Chrome`：透明角落控制、窗口操作及一个可取消的闲置淡出；`LyricsTransportView`：调用现有会话的播放 / 跳转控制。
- `KineticPosterLayout`：按旧版三栏结构排版，在词边界重排而非每帧生成字符串，仅保留一组文字布局。
- `LyricsFrameController`：一个可见场景仅一个时钟驱动，上限 60 fps，失焦 30 fps；暂停、隐藏、关闭动画、时钟过期与 detach 停止。减少动画保留准确高亮，关闭抬升及滚动位移。

播放会话仍为事实 owner。收到的原生位置用于显示；短时速度从连续确认位置估计，最多外推 250 ms，seek / 状态 / operation 变化清零。不根据 UI 时钟推进队列、确认 EOF 或推断设备事实。

## 验证与边界

短测程序：`tests/Echo.Lyrics.Smoke`。使用真实文件读取、设置文件、实际 Avalonia 控件与 Skia 渲染；音频命令由测试 Host 提供确认，不调用真实设备或旧曲库。输出在 `misc/lyrics-migration/`。

独立界面修订另通过 `misc/lyrics-migration/Echo.Lyrics.NativePreview.csproj` 检查当前歌词 C# 和 XAML。该隔离预览用于工作区其他模块尚在改动、整项目编译不稳定的阶段；仅把 XAML 的歌词类型限定到预览程序集，布局内容不变。数据依赖使用最近一次成功编译的程序集。它验证当前舞台自身，不证明当前整应用装配已经通过。截图及 `native-preview.out.log` 记录此来源。构建的一条 AVLN3001 提示来自 View 必须注入 owner、没有参数为空的运行时加载构造函数；View 由装配代码直接构造，已实际完成渲染。

已覆盖：格式时间线、词时间、译文/罗马音、文本数量限制与取消、真实 sidecar / 封面租约、全部 12 个样式与 22 个主题的截图、窄窗口 / 深色 / 纯文本 / 错误恢复、点击使用的跳转接口与双偏移、手动浏览、跨页面偏好与导入保留、最后一次设置写入、迟到结果丢弃及退出释放。帧驱动检查使用 UI 事件循环确认新位置启动、隐藏停止、过期停止及暂停停止。

截图是实际原生控件的 headless 渲染，示例歌曲与封面为夹具；不是用户真实歌曲或实际窗口/设备验收。未测真实设备长时间播放、实际呈现 fps、macOS/Linux、旧 Canvas 引擎逐项视觉等价或长期资源稳定性。构建/短测累计时间由 `misc/lyrics-migration/budget-ms.txt` 记录，预算 120 秒，单次命令不超过 60 秒。

回滚：移除 Shell 的歌词路由和 Bootstrap 歌词装配，恢复占位页。旧工作区、扫描与原生音频代码未修改；新应用设置文件可单独保留。

### 本轮证据状态

独立窗口内容宿主及返回恢复的整应用短测曾通过（final-smoke.out.log 的此前成功轮，4.279 秒）；随后增加 768 像素封面租约及对比度收尾。工作区并行改动持续更新公共输出，后续验证转移到 verified-build 独立目录。最新整应用检查仍受到未完成的歌单数据库接口和首页 XAML 等并行改动阻塞，不能把早先通过结果视作当前整工作区全部通过。768 / 320 双分辨率断言已加入，但最新通过结果尚未取得。

现有 PNG 来源为此前已经实际渲染的独立舞台版本；后续播放控制迁移正在同一工作区合入，预览不代表这部分最新并行改动已经完成验收。所有构建、短测和重跑累计约 92 秒，未执行长测试或打包。


原版保真度修订以 [LYRIC_STYLE_FIDELITY.md](LYRIC_STYLE_FIDELITY.md) 为准；样式入口和渲染数量不构成等价证明。

