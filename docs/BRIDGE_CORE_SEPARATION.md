# 桥核分离

日期：2026-10-19。目标：学习 Roon 的职责划分，逐步让桌面成为控制端，并让 Core 与输出端具备独立运行能力。

## 借鉴的边界

[Roon 官方架构](https://help.roonlabs.com/portal/en/kb/articles/architecture)将系统分为 Server、Control、Output：Server 拥有曲库、队列及多控制端的一致状态，Control 提交操作和显示结果，Output 输出音频。[Roon Bridge](https://help.roonlabs.com/portal/en/kb/articles/roonbridge)用于把另一台机器的音频设备提供为输出端。三种职责可以部署在同一台机器，也可以分开。

ECHO 借鉴这些所有权与部署边界。公开说明不是 Roon 的内部实现或 RAAT 协议规范。现阶段保留 ECHO 原生宿主中的解码、PCM、DSP、设备输出和时钟；网络输出确有需求时再设计有界音频传输，不为模仿 Roon 将 PCM 搬进托管控制进程。

明确区分两种桥：**共享网关**提供曲库发现和有权限的媒体传输；**音频输出桥**把输出设备接入 Core。二者不拥有曲库数据库，不各自推进同一 Zone 的队列，不能混成一个带数据库和播放器的第二套 Core。

## 目标归属

| 能力 | 唯一 owner | 控制端或桥的权限 |
| --- | --- | --- |
| 曲库、搜索索引、收藏、歌单、个人标签、历史、远端曲目索引 | Library Core | 分页查询及类型化命令；无 SQL、数据库文件或整库快照 |
| 扫描、目录监听、增量规则、任务取消与收尾 | Library Core | 提交、查询和显式取消任务 |
| 音源配置、凭据和刷新、播客进度、电台收藏 | 对应业务模块，由 Core 装配 | 控制端编辑，凭据不进入广播状态或普通诊断 |
| 队列、shuffle/repeat、切歌意图、恢复及统一后台准入 | Playback Core | 提交命令，订阅有界状态；控制端不得自行推进队列 |
| 解码、PCM、DSP、设备、实际播放位置、EOF/drain | Native Audio Host | 控制层发送请求并显示宿主确认 |
| 本地 IPC、启动与重连 | Core 客户端及应用组合入口 | 当前用户访问、版本握手；没有失败后另开数据库的退路 |
| LAN 发现、共享媒体的 Range 传输、协议适配 | 共享网关 | Core 授予的只读资源能力，不接收任意本机路径 |
| 输出设备能力与音频宿主连接 | 音频输出桥 | 带会话及代次的输出命令，回传实际设备与时钟事实 |
| 窗口、主题、歌词绘制、封面 GPU 缓存、快捷键 | Control / 平台适配 | 显示和用户交互，不拥有业务持久化生命周期 |

## 当前落地：进程内 Core 边界

本批增加 `src/Echo.Core`，没有新增包、端口、默认后台进程、数据库格式或媒体文件变更。

- `Library/LibraryCore` 从 Desktop 移入，持有同一份惰性 `LibraryDatabase`、扫描任务、扫描策略与目录监听。继续实现 `Echo.Contracts` 已有能力接口，分页、扫描事务、CUE/元数据读取及独立库格式沿用现有实现。
- `CoreRuntime` 装配曲库与已有 `PlaybackRuntime`，只建立一份 `ListeningRecorder`。记录器位于 `Echo.Application/Playback`，不依赖 UI；多个视图无需重复订阅并写入听歌历史。
- `App` 持有 Core，`DesktopComposition.CreateWindow(core: core)`借用它。视图关闭只解除自己的 Core 事件订阅；Core 在应用退出时统一释放。独立预览仍可创建自有 Core，或注入已有播放 runtime；借用的播放 runtime 不被预览释放。
- 惰性数据库初始化失败会释放已经打开的数据库；退出先取消、等待初始化、关闭 watcher、等待扫描，再释放数据库锁。重复释放等待同一关闭任务，防止窗口和应用交叉释放。
- 保留 16 个监听根、32 个待扫描根、有界扫描 worker/batch、现有 SQLite 通道预算；没有复制曲库缓存或新增常驻整库集合。Core 新增一份记录器订阅、取消源和等待任务，无轮询定时器。

当前依赖：`Desktop → Core → Application / Infrastructure → Contracts / Playback`，Core 不引用 Avalonia、Desktop 或具体平台程序集。Core 是具体装配和资源 owner；业务算法仍留在原模块，避免继续把业务放入一个万能 Core 文件。

**这完成的是模块与生命周期分离。** Core 仍嵌入桌面进程，退出应用仍退出 Core。尚不能称为独立 Server、远程 Control、音频 Bridge 或 Roon 式多房间播放。

## 剩余耦合与迁移顺序

| 阶段 | 当前证据 / 入口 | 接下来交付及退出条件 |
| --- | --- | --- |
| 1. 曲库与记录 owner | 已迁 `DesktopLibrary*`、`FolderWatchCoordinator`、`DesktopListeningRecorder` | 本批完成；真实小文件扫描及 owner 释放短测，桌面构建分别记录结果 |
| 2. 收拢其余生产生命周期 | `DesktopComposition` 仍创建 RemoteFileClients / RemoteSourceCatalog / PodcastLibrary / PodcastProgressWriter；窗口仍绑定 resolver | 把音源解析、凭据端口、播客写入和电台持久状态交给对应模块及 Core。控制端断开不撤销下一首音源，也不重复打开播客数据库；窗口设置和选择对话框留在 Desktop |
| 3. 拆出桥接模块与音频客户端 | `Features/Connect/Runtime` 的 Link、DLNA、MQTT、HQPlayer 依赖 PlayerViewModel / FeaturePreferences；`Echo.Playback/NativeAudio` 仍与编排同程序集 | 定义最小控制、只读媒体、输出端口；移出网络适配与 `Echo.Audio.Contracts` / `Echo.Audio.Client`，消除对 ViewModel 的依赖。保留 Native Host 播放事实及现有协议，不添加仅转发的空壳项目 |
| 4. 同用户独立 Core 服务 | `LibraryDatabase.OpenAsync` 生产打开点目前为 Core 与独立 CLI；Desktop 仍直接持有 PlaybackRuntime | 独立 `Echo.Core.Host`、本地客户端及版本化协议，Desktop/CLI/共享网关附着同一服务。核心业务能力全走明确契约，关闭/崩溃 Control 后已接受任务和播放继续；同库多启动争用只留下一个 owner |
| 5. 可选网络输出桥 | 当前没有跨机器音频会话/时钟/背压协议 | 输出端发现、显式配对与权限、能力协商、代次、断连行为、有界流和时钟校正；先单 Zone，再按实际需要考虑分组同步。单独做真实网络及设备验收 |

阶段 2/3 可以按单条业务边界推进；阶段 4 必须在生产持久化和任务入口归属齐备后切换默认路径。不要先加一个只支持少量查询的服务，再让其余命令悄悄直连数据库。

CLI 当前是独立离线入口，继续受数据库 writer lock 保护；它尚非共享 Core 客户端。设置中的备份/恢复和文件维护也要在阶段 4 前纳入独占维护协议，不能在 Core 活跃时绕过 owner 替换数据库。

## 服务化前固定的约束

- 以规范化曲库身份和当前用户定位服务；锁文件、PID 或端点存在不能替代实例握手。协议带版本、服务实例/epoch、请求 ID、取消及错误结果。Unix socket / named pipe 限当前用户，不默认开放管理 TCP。
- 服务共享数据库读写通道、后台准入和内存预算，连接只持有自己的有界 pending 和通知，不为每个 Control 复制完整 runtime。慢订阅者合并失效摘要，再按 revision 重取当前页。
- 浏览请求取消只取消本次等待。已接受的长期扫描以任务 ID 独立持有，关闭控制端不等同取消；用户显式取消和服务退出另有语义。当前 UI 导入仍传调用者 token，不能把本批误报为任务已脱离控制端。
- 收藏、歌单、文件维护等写入带操作 ID 和 revision；结果不明先查回执，不用新 ID 盲重放。沿用现有操作 ID/扫描事务，不为协议适配放松完整性。
- 全服务退出是单独管理动作。最后窗口退出、最后连接断开、活动扫描、watcher、媒体流分别记账；按需启动和有界空闲退出，不安装未请求的开机服务。
- 网络输出桥持有自己的实际设备连接，断连后旧响应失效；暂停、seek、EOF 和设备切换仍由宿主事实确认。控制消息不承载 PCM。

## 本批验证与回滚

聚焦检查入口：`dotnet run --project tests/Echo.Core.Smoke -c Release`。使用隔离临时 SQLite 和一个短 WAV，验证取消等待不破坏共享初始化、重复 writer 被拒、增量扫描、跨能力读取提交状态、监听释放、初始化/退出竞态及借用播放 owner 不被释放。音频宿主使用拒绝设备调用的替身，不代表真实音频设备或 UI 已验收。

桌面单独 Release 构建，验证模块接入和 AOT 兼容分析；这不等于 Native AOT 发布或 Windows/macOS/Linux 设备验收。验证总预算沿用开发文档，不追加全量或长测。

回滚本批恢复 Desktop 装配和原文件即可，无 schema 或配置迁移。回滚时旧记录器与新 Core 不能同时启用，避免重复历史写入；同一曲库也不能同时建立两个 runtime。

本轮结果：Core 聚焦检查通过（含恢复依赖与编译约 32.2 秒）。隔离分支桌面首次构建约 42.1 秒，受基线 `PodcastViewModel` 的 CS0173 与 `ShellSidebarEditing` 的 CS8604 阻断；主工作区这两份文件已有对应修复，临时使用其修复后桌面 Release 构建通过（约 27.2 秒，0 错误、9 项既有 Avalonia 警告），随后已还原这两份文件。借用修复不包含在本批变更中。累计验证约 101.5 秒，未执行实际 UI、真实音频设备、Native AOT 发布或跨机器验收。
