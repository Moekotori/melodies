# 收藏与歌单页面迁移

日期：2026-10-02。

2026-10-20 更新：文件导入/导出、跨重启删除恢复及 201–1000 项的 Core 跨页播放已经补入。以下为早期迁移记录；当前功能、资源预算、剩余差距与检查入口以 [ECHO 功能对照](ECHO_FUNCTIONAL_PARITY.md) 为准。

## 来源与交付

只读对照旧 `D:\ECHO` 工作区，HEAD `3dbf28993`（包含当前未提交样式）。结构依据 `SteamPlaylistsPage.tsx`、`CollectionPlaylistSidebar.tsx`、`PlaylistBrowseView.tsx`、`PlaylistFavoritesSummary.tsx`、`collection-playlists.css`、`playlist-collection-view.css` 和 `playlist-detail.css`。保留集合内侧栏、总览、喜欢歌曲/专辑、列表/卡片、封面详情头部、紧凑曲目表和右侧滚动条，使用当前 Avalonia 主题与中英文资源。

实际入口：主侧栏“收藏与歌单”。支持创建、名称/介绍编辑、曲库选歌、多选移除、手动上移/下移、歌曲收藏、搜索、数据库排序、分页、播放、随机播放、下一首/加入队列，以及歌单软删除和本次会话内撤销。喜欢专辑复用已有专辑墙与详情导航。

尚未迁入旧版智能歌单规则、歌单分组与拖拽整理、文件导入/导出、外部账户同步、自定义歌单封面和完整恢复记录管理。新歌单不自动读取旧版生产库。超过一页的播放按钮明确显示“播放本页”，当前播放/随机/追加以当前查询的最多 200 行为范围；整个超大歌单的连续供页尚未接入。CUE/osu 使用曲目契约中的实际音频路径和 CUE 范围，实机虚拟音源播放尚未验收。

## 职责与所有权

| 模块 | 职责、输入与输出 |
| --- | --- |
| `Echo.Contracts/Library/PlaylistModels.cs` | 独立歌单/条目 ID、分页 DTO、排序、取消和类型化命令端口 |
| `Echo.Infrastructure/Library/PlaylistSchema.cs` | 新格式 schema v11 增量；后续并行迁移继续递增版本 |
| `PlaylistQueries.cs` | 专属交互读取通道，有限分页、版本一致性、SQL 搜索与排序 |
| `PlaylistCommands.cs` | 既有 `_writer` 的唯一写入 owner，事务原子修改、独立 occurrence、软删除和提交回执 |
| `Echo.Desktop/Bootstrap/DesktopPlaylists.cs` | 同一个懒加载 DesktopLibrary 的歌单装配与转发，不建立第二连接 owner |
| `Features/Playlists` | 页面状态、原生视图、虚拟列表、选择器、封面租约及实际命令调用 |
| `ShellPlaylists.cs` | 本窗口的轻量导航/布局记忆与最近删除的一个 ID |

歌单列表、详情曲目、选择器查询分别最多 200 条，写入批次最多 200 条。浏览器在列表与卡片两种布局中都虚拟化行；封面只在附着的卡片和当前详情持有租约，共享既有 32 MiB 缓存。喜欢专辑使用既有有界分页缓存。UI 不执行同步数据库或文件 I/O，不驻留整库。播放只在当前曲目变化时刷新高亮，不订阅进度帧。

搜索防抖 120 ms、回车立即执行；读取支持排队取消与 SQLite 中断，generation 拒绝旧结果。写入支持排队取消和批次内检查，取消在事务提交前回滚。同一个 operation ID 和指纹复用已确认结果，最近 256 个回执和修改同事务持久化；不能依赖已淘汰回执重试历史操作，也不会自动生成新 ID 重放不明写入。

导航/关闭时取消请求、解除播放和语言订阅、释放选择器、专辑墙、详情、封面租约和控件滚动订阅。新格式升级前通过 SQLite Backup API 保存一致性副本；旧 ECHO 库仍由既有 application ID 校验拒绝直接打开。

## 本轮短测与限制

入口：`tools/Echo.Preview/PlaylistUiSmoke.cs` / `--playlists <misc 输出目录>`。本轮使用当前 Desktop 源码隔离副本、真实新格式 SQLite、真实 Avalonia Headless 控件输入和 Skia 渲染；音频确认使用模拟夹具，不是设备播放验收。

通过：创建回执幂等、添加回执幂等、重复歌曲 occurrence、手动换序、旧 revision 拒绝、取消、245 条歌单的 200/45 条分页、SQL 时长排序、喜欢状态、实际新建/选歌/保存按钮、删除撤销、播放队列来源、少于 35 个 realized 曲目行、总览卡片实际生成、明暗/英文/窄窗口渲染、表头与曲目列对齐、离页释放。

截图与日志在 `misc/playlist-migration/`，最终定向通过记录为 `ui-verified.log`。全部构建/检查和失败重跑累计约 110 秒，未执行全量测试、长时性能或打包。最后的完整当前工作区构建失败于并行歌词模块 `LyricsTransportView.SetCompact` 缺失；此前也受到历史页面尚未完成的编译阻断。最终歌单源码在冻结的其余 UI 和已构建依赖上编译成功；有 5 条其他页面的原有 XAML 运行时加载警告。不能将隔离短测称为整工作区构建通过，也不能将夹具截图称为旧版全部功能等价或三平台实机验收。

旧实现继续保留。回滚使用升级前新格式库备份与原版本应用；禁止把升级后的库交给不支持该版本的二进制，禁止删除或改写旧 ECHO 生产库。临时隔离副本和夹具均位于 misc，未写入凭据。
