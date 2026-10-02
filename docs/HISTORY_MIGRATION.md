# 历史页面迁移

日期：2026-10-02。入口：侧栏“历史”、主页历史入口、队列历史快捷入口。历史路由已由临时队列投影改为独立 SQLite 播放记录。

## 视觉重设计（2026-10-02）

收到首版视觉反馈后重做界面层次。主体是带封面的聆听时间线：两位日期数字与短强调线、68 px 曲目行、左侧时间、42 px 封面、歌曲/艺术家/来源、专辑及细进度条。选中仅影响对应歌曲，不把整段日期染色；详情收成分栏信息与可查看的路径。标题、两种视图、搜索/日期和主要播放动作分层；维护操作收进右上菜单，播放文案修正为实际的单曲操作。

回顾页采用四项简洁数字、易读的小时/分钟、一处趋势容器、细坐标线和蓝紫强度。小窗口隐藏专辑列、指标排成两列、歌曲/艺术家排名纵向排列。界面复用 EchoPage、EchoField、EchoHeading、EchoMuted、EchoAccent 等语义资源，支持现有主题；基准色来自原有 tokens（#f4f5f7、#181d26、#616977、#4b55e8，深色基准 #0c1016），没有锁死用户配色。标题和歌曲沿用 EchoFont / CJK 回退；时间数字使用 Segoe UI 的轻量排版。

封面调用原有 QueueArtwork / AlbumArtworkCache，32 MiB / 64 请求边界及可见视图租约保持生效；新 `ShowPlaceholder` 选项只用于历史的中性缺图图标，默认行为保留。没有新封面工作器、额外数据库读取、常驻动画或整库 UI 数据；选择仅通知旧/新两行。进度条显式取消框架默认最小宽度，按本列对齐。

本轮原生源码截图在 `misc/history-design/final/`：浅/深记录、展开详情、浅/深回顾，以及 960×760 的两种视图。封面和曲目为生成夹具，音频为协议模拟。5 万记录夹具的 21 项短测通过；单次首屏 25.63 ms、回顾 275.47 ms（不是性能分位数）。累计构建/失败重跑/短测约 **98.7 秒**。

当前运行的开发窗口锁定应用宿主输出，且并行筛选/网盘页面处于修改中，所以使用隔离源码副本验证；仅在副本里去除未完成筛选事件以及调整网盘中间状态绑定，不修改原工作区这些页面。历史目录全部源文件与成功渲染的副本 SHA-256 一致，共享 QueueArtwork 源码也直接复制。隔离桌面构建成功（其余页面 6 条既有 loader 警告），专用预览构建零警告。整工作区构建及真实用户窗口/设备仍需等待并行修改收口，不把此预览称为完整应用验收。

## 旧版来源与交付

只读对照 `D:/ECHO/src/renderer/pages/HistoryPage.tsx`、`pages/history/HistoryFilters.tsx`、`HistoryRecords.tsx`、`HistoryRecordRow.tsx`、`ListeningReview.tsx`、`ListeningActivity.tsx`、`listeningReviewData.ts`、对应 CSS，以及 `LibraryStore.ts` 的有效播放判定。

已接入原版两种视图、歌曲/艺术家/专辑搜索、全部/今天/本周/本月/今年/自定义日期、仅有效播放、按日记录与日汇总、时间/来源/进度/状态/展开详情、播放/下一首/加入队列、复制、打开所在文件夹、刷新、无效记录清理和不可撤销清空的页内确认。回顾有总量、日/月柱状趋势与时长/次数切换、日期钻取、歌曲/艺术家/专辑排名、等长已过去周期对比、会话/连续天数、首次与回访、重复与再发现、格式和音质。沿用窗口主题、语言、页面搜索快捷键和页面生命周期。

有效播放沿用旧版：结束后听满 30 秒，或达到曲长 50%。暂停保留同一 occurrence；seek 不将跨越的音频位置计入时长，也不因原生 operation ID 改变生成第二条记录。播放事实来自现有会话 owner；历史页不持有或更新播放时钟。

## 模块和资源

| 位置 | 职责、所有权与限制 |
| --- | --- |
| `Echo.Contracts/Library/HistoryModels.cs` | 筛选、双向游标、记录、日汇总、回顾与历史端口。 |
| `Echo.Infrastructure/Library/HistoryQueries.cs` | 交互读通道、最多 200 条记录、时间/ID keyset 索引、rowid 新增快照、epoch 校验；只有首屏读取总数。可中断正在执行的 SQLite 读取。 |
| `HistoryReviewQueries.cs` | 后台读通道执行 SQL 聚合，给列表/导航保留交互通道；最多 366 日、每类 8 个排名。SQLite 窗口计算会话和连续天数，不把事件表交给 UI。 |
| `HistoryMaintenance.cs` | 同一个既有 `_writer`。清空先事务提交新 epoch，立即隐藏旧数据并拒绝旧 recorder 的迟到写入；随后每批 200 条清理并让出通道。取消可能留下隐藏待清理行，下次清空可继续清理。无效清理保留未结束事件。 |
| `HistorySchema.cs` / `HistoryQualitySchema.cs` | v14 历史快照/FTS/epoch；v16 音质快照。已有库迁移前 SQLite 一致性备份。中间 v15 专辑衍生统计属于并行专辑模块，本迁移保留它。 |
| `DesktopListeningRecorder.cs` | 一个分类订阅、一个消费工作器、一次等待写入。开始/退出播放/结束时保存，正常播放最多每 15 秒刷新一次持久化秒数；每次新 occurrence 取得 epoch。应用生命周期负责最后保存和释放。 |
| `Features/History/*` | ViewModel 管当前 200 条、两个游标与一个回顾 DTO；列表有有限视口和虚拟化，不缓存全部页游标。趋势按跨度合为最多 45 日柱或约 13 月柱；没有定时器或每格控件。请求 generation、防抖 120 ms、取消和迟到结果淘汰；离页释放集合和语言订阅。 |
| `DesktopHistory.cs` / `ShellHistory.cs` / 主窗口 | 同一 DesktopLibrary 装配、历史路由及现有页面搜索入口。无第二数据库 owner。 |

新记录在首次插入时保存标题、艺术家、专辑、路径、时长、编码、采样率、位深和来源；更新播放秒数不重建 FTS。曲库改名不会改写历史快照。存在的曲目用当前路径与 CUE source 重放；缺失曲目保留快照但禁用播放操作。

## 短验证与证据

累计构建、失败重跑、数据库短测和原生 UI 短测约 **109.6 秒**，每条均设置小于 60 秒的外部进程上限。日志和临时数据仅在 `misc/history-validation/`，无凭据。机器为 Windows 10.0.26200、AMD Ryzen 9 9950X3D；数据库定向检查使用 Release，原生控件预览使用 Debug/Skia Headless，不能外推到文档的 4 核参考设备。

- 当前历史源码所在 Desktop 定向构建通过；5 条已有的其他窗口/抽屉运行时 loader 警告单列。独立历史预览构建 0 警告、0 错误。完整通用预览工具受并行 `SongDropSmoke` 接口修改影响，故提供只运行本功能的 `tools/Echo.History.Preview`。
- 隔离真实 SQLite：50,000 条生成记录、一个曲目，17 项边界检查；v13→v16 真实结构升级额外确认 ID、快照、计数规则和升级备份。最后一次数据库检查使用已构建的 Release 基础设施：再次编译基础设施时出现并行 `SelectedFileDiscovery/OsuFolderExpansion` 新参数不一致，未修改该扫描链路。
- 原生 Avalonia Headless/Skia：50,000 条生成记录、8 个曲目，21 项检查；实际 realized 行数小于 40，当前 DTO 为 200 条。覆盖双向分页、搜索、有效筛选、队列、实际鼠标点回顾、双击详情不误播、日期钻取、日期错误、确认取消/清空、旧 epoch 失效、页面退出释放。
- 真实 Desktop recorder + 模拟 Native Audio 事件：开始、seek、暂停、停止确认只保存一个 occurrence，跳转不增加对应聆听秒数；清空后旧 recorder 写入不复活。
- 最后一次 UI 夹具首屏 **23.46 ms**，回顾聚合 **400.61 ms**；同轮其他样本约 7–31 ms / 319–623 ms。样本很少且机器上存在并行构建，不能当作 p95、性能 SLA 或完整旧版对照。统计始终在后台读通道。
- 最终实际产品控件截图：`misc/history-validation/ui-recorder-final/history-light.png`、`history-dark.png`、`history-review-light.png`、`history-review-dark.png`、`history-compact.png`。1440×900 及 960×760、96 DPI、Skia Headless 渲染；不是 HTML 重画。

验证入口：`dotnet run --project tests/Echo.Library.Smoke -c Release -- history misc/history-check`；`dotnet run --project tools/Echo.History.Preview -- misc/history-preview`。开发者仍需在执行时自行应用 60 秒/120 秒预算；本轮已停止追加验证。

## 保留边界与回滚

旧 ECHO 工作区和生产库只读、不写入。尚未提供旧生产历史库导入器、远程/流媒体历史、原版任意数字页跳转/完整旧版全部行为。趋势和记录按本机日期一致分组，旧版回顾的 UTC 图表日期不是逐像素复刻。无效刷新以曲库缺失/删除事实为准，先扫描可更新文件状态，不在历史页同步遍历磁盘。早期新格式库缺少结束时间，迁移使用最后保存时间并从仍存在曲目回填元数据，不能恢复已经丢失的历史快照。

尚未验收真实音频输出、用户实机输入与长期内存/帧率、macOS/Linux、Release/AOT 发行。新增页可从主窗口路由移除而不影响普通队列/扫描/音频。数据库升级不可直接交给旧新格式二进制打开；回退需停止当前 owner 后恢复对应迁移备份，不打开旧 ECHO 生产库写入。
