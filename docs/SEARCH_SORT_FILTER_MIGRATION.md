# 搜索、排序与筛选迁移

日期：2026-10-02。普通扫描和播放设备实现不属于本增量；只使用新格式曲库和生成的隔离夹具。

## 已接入的路径

- 曲目页：分页读取、搜索、清空/回车/Esc、组合筛选、最多三级排序、升降序、收藏、播放本页、下一首、加入队列、共用扫描 owner 的文件夹导入和取消。
- 专辑与艺术家：复用组合筛选；保留各自原有分页、排序、分组、详情和封面生命周期。文件夹内曲目复用相同搜索编译器。
- 全局搜索：标题栏真实入口与可配置的 `openGlobalSearch` 快捷键；`openSearch` 保留当前页面搜索。完整结果页为 `search` 路由。并行的全局弹层接入同一查询端口和 SQL 编译器，弹层自己的结果/动效生命周期另归对应模块。
- 搜索命令：`/title`、`/artist`（包括专辑艺术家）、`/album`、`/genre`、`/path`、`/year`、`/exact`、`/-` / `/not`，及中文别名；支持空格、引号和冒号参数。专辑支持 `/decade 1990s`、`/genre Pop`、`/format FLAC`、`/format 24bit`、`/format 96k`，与筛选面板取交集。
- 归一化：NFKC、拉丁前缀/精确短语、CJK 子串、OpenCC 简繁变体。词项取交集，排除取反，FTS 标点和 LIKE 通配符均转义，用户文本只作为绑定参数。
- 曲目排序：曲名、艺术家、专辑、碟号/曲序、年份、时长、采样率、位深、编码、最近更新、文件修改时间、入库时间。最多三个不同优先级；重复选择同一字段只保留首次优先级。以稳定 ID 收尾。
- 筛选：文件格式、标准采样率/DSD/其他、16/24/32 bit、年份、完整流派名称、文件夹及子目录、收藏、无损、普通文件/CUE/osu!。专辑的收藏使用专辑个人状态，曲目和艺术家成员使用曲目收藏。
- 记忆：三个页面的排序和筛选随既有桌面设置持久化；搜索词不写设置。返回页面恢复条件。

## 来源与职责

对照旧工作区的 `core/Echo.Core/Search/{LibrarySearchParser,ChineseSearchVariants,SearchUnicodeRanges}`、`src/shared/library/{librarySearchQuery,albumSearchQuery}.ts`、`src/main/library/unifiedTrackQueryFilters.ts` 及 `src/renderer/pages/songs/{songSortMenuConfig,songsFilterOptions}`。移植并适配 C# 搜索解析与 OpenCC 数据，保留数据许可和第三方说明。旧项目及旧生产库没有写入。

| 落点 | 职责及资源 owner |
| --- | --- |
| Contracts/Library/LibraryQueryOptions | 类型化查询、排序和筛选；无 UI、SQL 或 I/O |
| Infrastructure/Search | 纯解析、查询归一化、受信 SQL 列表、绑定参数；包内 OpenCC 字典延迟加载且不缓存用户查询 |
| Infrastructure/Library | SQLite 交互读 lane、排序、过滤、事务版本、游标、计数缓存；写入仍由已有唯一 writer 负责 |
| Desktop/Features/LibraryDetails/LibraryFilter* | 页面自己的小型输入状态和共用面板；不持有数据连接或整库 |
| Desktop/Features/Songs | 当前页 DTO、选择、导入任务和播放命令；可见页最多 200 首 |
| Desktop/Features/Search/LibrarySearch* | 完整结果页，每类最多 30 项；离开时取消并清空 |
| ShellQueryPreferences / WindowPreferencesBinding | 最多三个页面的条件快照；沿用已有有界、合并保存的设置 writer |

SQL 不在 UI 线程执行。搜索防抖 120 ms，页面拥有一个请求 CTS 和递增 generation；取消或迟到请求不能写回。`ReadCancellation` 仅在连接被本次读取占用期间注册 `sqlite3_interrupt`，结束后注销，避免取消下一条查询。扫描读取和交互读取沿用不同 lane。

页面查询一次最多 200 行；专辑/艺术家保留现有最多三个分页缓存；每个计数缓存最多 16 项；已有数据库队列/准入边界不扩大。完整搜索页最多 90 个小型结果。JSON 设置原有文件上限保持不变，新条件数组最多三项，路径/流派/搜索分别限制为 1024/128/128 字符。

## 索引与一致性

新格式数据库 v8 加入六字段规范化 FTS、CJK trigram 索引、不可变入库时间和常用曲目排序索引。后续并行模块可以继续递增 schema 版本；v8 迁移仍在同一 writer 初始化事务链执行，不打开旧 ECHO 库。

FTS 增量维护与曲目修改在相同事务中提交。三个及以上 CJK 字符使用 trigram LIKE；一两个字符或含转义通配符的搜索可能需要扫描候选索引，不能据此声称所有查询满足搜索预算。短 CJK、实体子串、任意多级排序和深 offset 的大库性能仍需实测。

曲目翻页绑定曲库 revision 和完整查询签名。改词、改排序/筛选后不能复用原游标；曲库变化需重新取首页。标题升序沿用 keyset；其他组合使用 revision 约束的 offset。SQL 内 count 和当前页使用同一读事务。组合专辑/艺术家条件必须命中同一条可用成员曲目，避免不同成员分别满足条件产生假交集。

收藏写入和 revision 同事务提交，因此相关计数缓存和旧游标不会在收藏变更后继续有效。并行专辑浏览 schema 的 SQL 片段补充了拼接边界空白，避免 `BEGINUPDATE` 导致初始化失败。

## 本轮验证

- `tests/Echo.Library.Smoke SearchProbe`：430 首生成数据，命令/内联引号、精确/排除、NFKC、简繁及短 CJK、专辑年代/格式、组合条件及同一成员交集、三种本地来源、12 种排序的两种方向完整分页、游标条件变化、收藏计数失效、预取消、生成 v5 夹具回填及 missing 隔离、设置 JSON 往返通过。最终日志：`misc/search-migration/query-complete.log`。
- 本轮较早的可编译窗口快照：原生 Avalonia Headless 验证有界翻页、绑定命令搜索、共用筛选、多级排序、导航恢复、快速改词迟到抑制、三类结果及页面释放。原生截图位于 `misc/search-migration/previews/`，不是 HTML 重绘。日志：`misc/search-migration/ui.log`。
- 后续补充：紧凑时长、长曲名裁剪、工具栏当前排序提示，以及标题栏弹层/模拟播放检查。当前源码还包含其他任务持续写入的常驻窗口和歌单代码，最终整桌面编译/上述新增检查以最新日志为准，不把较早窗口短测当成最终混合工作区全部通过。
- 最终整桌面重跑仍受并行历史页缺少 `HistoryReviewDisplay` 阻塞，见 `misc/search-migration/desktop-final-scoped.log`；未修改这些页面来掩盖错误。
- 所有命令预设 12–55 秒超时；只终止自身进程树，累计构建、失败重跑和 smoke 控制在 120 秒内。构建竞争时使用 `misc/` 下中间输出，不结束其他构建或应用。

这是小型行为验证，不是 5 万曲目 Release 性能、真实音频设备、真实交互或三平台验收。播放检查使用模拟控制端时只证明队列与 UI 命令接线，不证明音频输出。

## 仍未等价迁移的旧版能力

网盘账号/远程曲库、拼音与日文罗马音索引、旧版自然语言/数值排序键、重复文件分组、BPM/码率/质量及完整听歌历史排序、跨曲库全量相关性排名、动态流派和目录选择器仍有独立数据或运行时依赖。当前面板不提供会返回伪数据的网盘选项。专辑与其他页面的并行扩展不属于本日志的验证结论。

## 回滚

可撤下对应页面和查询装配入口；停止本次启动的验证程序即可。新索引是新数据库的向前迁移，保留新库及其正常备份，不降低 `user_version`，不直接删除索引或用旧程序强行打开更高版本 schema。测试文件只位于 `misc/search-migration/`，不含凭据。

## 旧版视觉对齐增量

2026-10-02 按用户“太丑了，做漂亮点，参考老版本”重做曲目页。参考旧工作区的 `misc/portrait-page-songs.png`、`src/renderer/styles/songs.css`、`songs-filter.css`、`songs-sort.css` 和 `SongsPage.tsx` 的实际结构。

- 封面、曲名、艺人/专辑和音质标签构成曲目行；时长靠右，播放/收藏/队列操作放入封面悬停及菜单；宽窗口保留已有流派列，窄窗口收起。
- 工具栏一行排列搜索、曲库操作、筛选和当前排序；搜索命令通过 `/` 入口展开，清空搜索与原键盘行为保留。
- 筛选改为左右对齐的条件行和细分隔线；排序按浏览/音频/曲库分组，支持方向和最多三层优先级。
- 曲目选择使用独立 ListBoxItem theme，避免 Fluent 默认蓝色块。现有语义色、字体和浅深色资源继续使用；不修改数据库/扫描/原生音频实现。
- 仅可见行租用 96px 缩略图，沿用现有 32MiB/192 项缓存、固定 worker 和有界请求。切页/回收取消请求、注销播放资料观察、释放租约及占位图绑定。加载真实封面时注销占位图资源绑定，主题切换不能把已加载封面盖回占位图。

验证入口：`tools/Echo.SongsPreview` 独立引用实际 Desktop 和原生 Avalonia Headless，隔离于通用预览工具正在修改的拖入测试。218 条演示数据只写入 `misc/songs-visual-refresh/`，预览音频控制端为模拟端，不读取生产库或打开真实设备。

定向 Desktop 编译通过（0 错误，5 条既有参数构造窗口的 AVLN3001 警告）；独立预览工具 0 警告/0 错误。可见行虚拟化、封面播放入口、嵌套筛选保持打开并实际应用、三级排序、封面跨主题保持、900px 窄窗口和导航释放通过。构建、失败重跑及短测命令实耗累计约 64 秒，未跑扫描长测或完整打包。

最新实际窗口截图：`misc/songs-visual-refresh/previews-verified/{songs-light,songs-dark,songs-filter-light,songs-filter-dark,songs-sort-light,songs-narrow}.png`。日志：`artwork-build.log`、`preview-verified.log`、`ui-verified.log`。这些是演示曲库的原生窗口截图；设备播放、用户实际曲库与跨平台验收仍需分别确认。并行任务的标题栏、侧栏及品牌变化只作为截图时的真实工作区背景，不属于本次视觉修改范围。

## 用户截图逐项对齐（2026-10-02）

以用户本轮提供的筛选面板截图为准。参考图约为 175% 缩放；恢复约 366 DIP 外宽、42 DIP 条件行、14 DIP 标题、11 DIP 标签和 12 DIP 半粗值文字。默认六行按“条件、文件夹、音频格式、采样率、年份、流派”排列；默认文案改为“全部歌曲、全部文件夹、不限格式、不限采样率、不限年份、不限流派”。标题保留“筛选 / 可同时组合多个条件 / 重置”，薄边框、小圆角和中性灰正文层级按参考图处理。

来源、位深和自定义年份/流派输入保留在更多筛选中，可由曲目工具栏的“…”菜单或面板右键菜单打开。条件可选收藏、无损及两者交集。年份使用有界选项；流派与文件夹在展开时最多查询 128 项，当前值保留，浏览其他文件夹继续使用原生选择器；回填选项不关闭正在打开的子菜单，detach 取消读取并移除属性观察。

设置了筛选专用 presenter、选择控件和中性明暗文字资源，避免基础主题切换改变参考图的关键文字/布局规则。未修改数据库、扫描或原生音频 owner。

验证：Desktop 定向编译 0 错误（保留六条其他参数构造视图的 AVLN3001 警告），独立工具编译 0 错误；原生 175% 参考窗口、明暗/重置/组合条件/年份、更多筛选，以及实际曲目窗口的异步流派选项、嵌套展开保持、查询应用、窄窗口和释放通过。本轮累计命令实耗约 108 秒，含失败重跑，未继续扩大验证。此前一次旧预览入口误启动生成的临时数据已移入 `misc/filter-reference/obsolete-preview-run/`。

参考比例截图：`misc/filter-reference/delivery/filter-reference-light.png` 与 `filter-reference-dark.png`；实际混合工作区演示窗口：`misc/filter-reference/delivery-window/`。所有图片为 Avalonia 实际控件渲染；截图中的标题栏/侧栏/基础主题可能包含并行任务变更，用户实机验收仍单列。日志：`local-theme-build.log`、`delivery-ui.log`、`delivery-window.log`。
