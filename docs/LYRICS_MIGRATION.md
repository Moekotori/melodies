# 歌词界面迁移

日期：2026-10-02。入口：播放栏「歌词」。所有内置样式 ID 保持与旧版注册表一致。

## 可运行能力

| 样式 ID | 名称 | 原生呈现 |
| --- | --- | --- |
| default | 简约 | 留白、上下文滚动与局部高亮 |
| luminous | 浮光 | 暗色舞台、柔和光束，播放时光束最多 30 fps |
| afterglow | 余音 | 放大的当前句、当前词轻微抬升、下一句 |
| afterglowCover | 余晖 | 左侧封面、暖色底与右侧阅读列 |
| editorial | 纸笺 | 纸张底色、衬线字、行号边栏 |
| folded | 折光 | 青紫光场、折射几何与阅读列 |
| roseVinyl | 留声 | 唱片、封面标签与歌词 |
| cinemaStage | 映幕 | 银幕遮罩、居中字幕构图 |
| kineticPoster | 跃字 | 大字、海报字体、轻微倾斜 |
| coverStage | 画境 | 封面铺陈与左侧阅读列 |
| cutBoard | 切影 | 斜切封面、切片与底部边线；原隐藏样式也可选 |
| jizura | Afterglow | 独立主题场景、当前句与逐词绘制 |

Afterglow 保留 22 个主题 ID 及主要色板：mist、night、dusk、paper、aozora、neon、matsuri、kabuki、yuki、junkissa、hokago、mahou、neko、dotto、echoes、time、brick、psyche、code、math、icpc、block。原生绘制为每个方向提供相应的雾光、星空、鸟居、和纸、云海、雨城、灯笼、扇骨、雪景、咖啡、校园、猫、像素、回声、时钟、积木、代码或图形意象。

这是原生版本的呈现迁移，**不代表旧 JIZURA Canvas 引擎全部场景、随机规划器、算法演示、特效和场景包已经逐项等价移植**。没有引入浏览器或把旧 Renderer 常驻在新应用内。创意工坊自定义歌词主题、MV、桌面悬浮歌词窗口、在线匹配与歌词编辑器仍属于独立能力。

歌词来源：同名本地 LRC、增强 LRC、YRC、TTML、SRT、VTT、TXT，或文件选择器导入。相同时间戳的 LRC 次行显示为译文；TTML 读取翻译、罗马音角色与真实词时间。没有真实词时间时整句着色，不伪造逐字时间。导入选项在当前窗口、当前曲目内跨页面保留，不写音源目录，不建立全库文件映射。

显示设置包含主字号、次字号、字体、对齐、上下文、译文、罗马音、逐字高亮、时间偏移与 Afterglow 主题。独立 JSON 文件保存于新应用的 `ECHO Avalonia/lyrics-settings.json`，不触碰旧生产库。字体列表使用本机字体与后备字体，不下载字体。正偏移让歌词提前；跳转同时扣除文件 offset 与显示 offset。

滚轮、方向键、PageUp/PageDown 和右侧滚动条浏览歌词；回车或点击有时间的行跳转播放；Home 或「回到当前歌词」恢复跟随。空歌词、加载、纯文本与格式错误都有明确状态及重新读取入口。

## 职责与资源所有权

- `Echo.Contracts/Lyrics`：纯文档、词时间、读取与设置端口；二分定位当前行。
- `Echo.Application/Lyrics`：纯解析及标准化；后台调用，支持取消。
- `Echo.Infrastructure/Lyrics`：一个读取准入器，最多 1 MiB 文件 / 512 KiB 文本；设置使用容量 1 的最新值队列和单写入者。
- `LyricsViewModel`：只持有当前文档、一次可取消加载、合并后的最新播放快照和一个共享封面租约。退出、换曲或新请求使旧结果失效。
- `LyricsAppearance` / `LyricsSelection`：窗口所属的显示偏好及两个路径组成的当前导入选择，无全文或全库缓存。
- `LyricsViewportControl`：当前行附近绘制、点击命中、浏览与字体排版缓存；无逐词控件。排版缓存最多 18 项，估算上限 4 MiB，字体/宽度/DPI/曲目变化失效。
- `LyricsBackdropControl`：独立保留背景层；词帧不重建封面或场景。`LyricsStagePainter` 与 `AfterglowScenePainter` 分别维护普通样式和 Afterglow 意象。
- `LyricsFrameController`：一个可见场景仅一个时钟驱动，上限 60 fps，失焦 30 fps；暂停、隐藏、关闭动画、时钟过期与 detach 停止。减少动画保留准确高亮，关闭抬升及滚动位移。

播放会话仍为事实 owner。收到的原生位置用于显示；短时速度从连续确认位置估计，最多外推 250 ms，seek / 状态 / operation 变化清零。不根据 UI 时钟推进队列、确认 EOF 或推断设备事实。

## 验证与边界

短测程序：`tests/Echo.Lyrics.Smoke`。使用真实文件读取、设置文件、实际 Avalonia 控件与 Skia 渲染；音频命令由测试 Host 提供确认，不调用真实设备或旧曲库。输出在 `misc/lyrics-migration/`。

已覆盖：格式时间线、词时间、译文/罗马音、文本数量限制与取消、真实 sidecar / 封面租约、全部 12 个样式与 22 个主题的截图、窄窗口 / 深色 / 纯文本 / 错误恢复、点击使用的跳转接口与双偏移、手动浏览、跨页面偏好与导入保留、最后一次设置写入、迟到结果丢弃及退出释放。帧驱动检查使用 UI 事件循环确认新位置启动、隐藏停止、过期停止及暂停停止。

截图是实际原生控件的 headless 渲染，示例歌曲与封面为夹具；不是用户真实歌曲或实际窗口/设备验收。未测真实设备长时间播放、实际呈现 fps、macOS/Linux、旧 Canvas 引擎逐项视觉等价或长期资源稳定性。构建/短测累计时间由 `misc/lyrics-migration/budget-ms.txt` 记录，预算 120 秒，单次命令不超过 60 秒。

回滚：移除 Shell 的歌词路由和 Bootstrap 歌词装配，恢复占位页。旧工作区、扫描与原生音频代码未修改；新应用设置文件可单独保留。
