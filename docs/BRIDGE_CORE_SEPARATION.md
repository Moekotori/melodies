# Core、Control 与 Bridge 分离

更新：2026-10-19。当前分支的默认生产入口已切换为独立 Core 客户端；不再使用窗口内嵌曲库和播放 owner。该文档取代本文件早期的“仅进程内抽取”状态。

当前整体仍是阶段性迁移：网络 Output V1 仍在输出端解码/DSP，原生处理端到输出端的加密传输、Core 侧 DSP 与时钟校正尚未完成。已新增[原生 PCM 数据面第一阶段](NATIVE_PCM_DATA_PLANE.md)，不能据此宣称桥核分离完成。

## 运行边界

```text
Echo.Desktop / Echo.Cli / Echo.Player
       │  当前用户的版本化本地 IPC
       ▼
Echo.Core.Host ── CoreRuntime
       ├─ LibraryCore → 唯一 SQLite owner、扫描、watcher、索引、收藏、歌单、历史
       ├─ PlaybackRuntime → 唯一 Zone/队列/会话、checkpoint、任务准入、sleep timer
       ├─ 音源配置与刷新、播客进度、电台元数据、OBS、本地及定时备份
       └─ Echo.Audio.Client → 独立 Native Audio Host

Echo.Desktop ── Bridge 客户端 IPC ── Echo.Bridge.Host
                                     │
                                     ├─ DLNA、手机遥控、MQTT、HQPlayer、只读 Range 传输
                                     └─ 受限 Core 客户端 → 同一个 Core owner
```

Bridge 按需启动，只有启用了保存的服务或用户执行连接功能时才创建进程。Core、Bridge 和音频宿主各有独立入口。Control 断开只释放本连接和展示资源；已经接受的任务、播放和已启用的桥接服务继续运行。显式停止播放、取消任务、停止共享和关闭 Core 分开定义。

Core 空闲退出同时检查连接、播放、已接受任务、目录监听、OBS 输出和定时备份，不以窗口数量决定退出。默认空闲期限为 60 秒，显式关闭走管理命令。显式网络监听且仍有有效配对时，Core 保持可供远程接入，撤销全部配对后恢复普通空闲规则。没有安装系统服务或开机启动项。暂停队列通过 checkpoint 保存后可以空闲退出；活动 watcher 和已配置的定时备份保留 Core。

参考 [Roon 架构](https://help.roonlabs.com/portal/en/kb/articles/architecture)的 Server／Control／Output 分工和 [Roon Bridge](https://help.roonlabs.com/portal/en/kb/articles/roonbridge)的部署思想。这里实现的是 ECHO 现有业务和现有连接协议的服务化，**不是 RAAT 兼容实现**。Core 默认只开放当前用户本地 IPC；已增加显式启用、配对和证书固定的 [TLS 远程 Control](REMOTE_CONTROL.md)。已增加完整文件 PCM 的 [网络 Output V1](NETWORK_OUTPUT.md) 代码路径；真实原生设备/双机验收、多 Zone 分组同步尚未完成。现有 DLNA/HQPlayer 送出仍使用其既有协议，不能将其验收推断成原生网络输出已完成。

## 项目和依赖

| 项目 | 职责 |
| --- | --- |
| Echo.Contracts | 现有曲库、音源、播客、电台、设置及备份业务契约 |
| Echo.Playback.Contracts | 会话、Zone、状态订阅、文件解析、系统集成端口及 runtime 接口 |
| Echo.Audio.Contracts / Echo.Audio.Client | 原生音频协议/设备契约，以及独立宿主连接与恢复 |
| Echo.Playback | Core 内的队列、会话编排和原生播放事实处理 |
| Echo.Application | 依赖会话接口的业务协调，不引用具体 Playback 实现 |
| Echo.Core.Contracts / Echo.Core.Transport | 版本化 DTO、能力表、帧格式、同用户连接、取消和容量限制 |
| Echo.Core / Echo.Core.Host | 业务组合、资源 owner、静态分派、任务及独立运行入口 |
| Echo.Core.Client | Desktop、CLI 和 Bridge 使用的类型化代理及展示投影 |
| Echo.Bridge.Contracts / Echo.Bridge / Echo.Bridge.Host | 连接服务契约、与 ViewModel 无关的协议实现及独立入口 |
| Echo.Desktop | 客户端装配、轻量 ViewModel、分页结果、绘制缓存和系统交互 |

Desktop 不引用 Core 实现、具体 Playback 或 Audio.Client。Bridge.Host 不引用 Desktop、Core 实现或 SQLite Infrastructure。Host 在桌面的项目引用中标为 `ReferenceOutputAssembly=false`，仅属于构建和部署关系。`tools/architecture/check_boundaries.py` 按传递依赖检查这些边界，避免以后重新从 UI 打开数据库或启动播放器。

协议代理由 `tools/architecture/generate_core_rpc.py` 生成，能力清单位于同目录的 `core-rpc-capabilities.json`。生成器核对批准的方法和签名，新增 public 方法不会自动导出；变更签名必须审查清单与协议版本。运行时不扫描程序集、不反射调用任意方法，也不序列化委托。RadioCatalog 的客户端更新委托只在当前 Control 内生成待提交列表，跨进程传输的是带期望 revision 的数据。

## 唯一 owner 与退出

- LibraryCore 保留现有独立库 schema、SQLite 事务、查询分页、扫描/CUE/元数据语义及资源预算。主曲库不由 Desktop、CLI 或 Bridge 直接打开。
- Core 统一持有音源路由及凭据刷新、播客数据库与进度 writer、听歌记录器、电台 ICY reader、睡眠定时器和系统媒体控制。Control 打开系统授权浏览器属于交互过程，授权结果通过 Core 保存。
- 文件选取/CUE 展开由 Core 文件解析端口完成。一次拖入最多 256 项，默认桌面将整批输入提交为一个 Core 任务，关闭视图不会丢弃其后尚未扫描的输入。系统文件激活同样交给 Core 接受为导入任务。
- 扫描、远端索引、资源检查和跨根目录维护使用任务 ID。Control 导航、退出或取消等待不取消已接受任务；用户按取消按钮才发送任务取消命令。任务只保留最近 16 个有界状态/报告，完整扫描记录仍由原数据库维护。
- Core 保留原生宿主的设备、实际位置、render、EOF/drain 事实；关闭窗口不会启动第二条推进队列。控制命令返回前接入宿主确认状态；低频推送播放状态，普通时钟更新不重复发送整个队列。
- 相同曲库使用规范化路径（包含符号链接解析）、服务锁和数据库 writer lock；用户数据目录另有 profile lock，避免不同库同时争用同一份播放/播客状态。Core 就绪前已取得数据库 owner。
- 退出先解除连接与任务入口，取消并等待后台任务，再保存并释放播放、音源和数据库。重复释放等待相同 owner 的关闭过程。Windows 防睡眠在 Core 的按需专用线程上申请和释放，不依赖 Avalonia dispatcher。

## 协议、权限和预算

| 项目 | V1 限制/行为 |
| --- | --- |
| IPC 身份 | 当前用户 named pipe；Unix 使用短端点名适应 macOS socket 路径限制；完整曲库身份仍在握手中核对 |
| 握手 | 协议版本、曲库身份、角色、客户端身份、Core instance/epoch、单调时钟频率 |
| 请求身份 | 本连接请求 ID + Core instance；旧实例请求拒绝执行，迟到响应不会覆盖新实例状态 |
| 连接 | 最多 8 个；握手前准入，本地 IPC 与 TLS 共用预算，满额不摧毁现有 owner |
| pending | 每连接最多 8 个，其中普通数据最多 6 个；服务保留 8 个播放控制通道，普通请求最多 16 个 |
| 大小 | 握手 4 KiB、请求 1 MiB、完整响应帧 8 MiB；长度在分配消息缓冲前校验 |
| 订阅 | 每连接一个合并状态信号、最多 16 条轻量通知；播放状态推送最多约 10 Hz，队列只在 revision 变化时发送 |
| 资源 | Core 大资源读取最多 2 路；既有封面 4 MiB 上限和扫描 worker/batch/SQLite 预算保留 |
| 慢客户端 | 写入期限 5 秒，超时释放本连接，不阻塞 writer 或其他控制端 |
| 任务 | 单个接受中的生产任务，最多保留 16 份状态；任务取消独立于请求/连接取消 |
| 重连 | 显式重试最多 3 次；连接失败显示服务故障，绝不回退直连 SQL。重连重新握手并接入权威快照，不自动重放写入 |
| 写入不明 | 返回有结果不明标记的错误；歌单沿用原操作 ID/回执，任务沿用任务 ID；不会生成新 ID 盲重做 |

Bridge 角色只能访问列出的播放控制和分页只读能力，不能读取来源凭据、改写曲库、配置定时器或关闭 Core。其播放投影只含当前曲目，移除 headers、历史、完整队列、内部处理配置及诊断。LAN 处理器仅转换既有的明确操作，不提供通用 Core RPC 转发。

封面等大资源与控制请求分开准入，PCM 不进入 Control 或普通 JSON IPC。队列仍限制 1000 项，目录监听仍限制 16 根/32 个待处理根，远端遍历保留原并发上限并采用明确有界 channel。没有增加整库快照、默认扫描、额外常驻 UI 定时器或生产第三方依赖。不以这些静态预算代替内存/延迟实测。

## 数据与兼容

数据库 schema 和播放 checkpoint JSON 形状沿用现有实现。新增 `core-settings.json`、`bridge-settings.json` 分别归对应服务，首次从旧 UI 偏好提取相关项；界面、主题、快捷键和语言仍归 Control。来源凭据沿用平台保护适配，平台保护尚未支持的能力不冒充可用。

备份创建、校验和定时调度在 Core；旧版备份仍可验证。新备份同时包含存在的播客、电台、Core 与 Bridge 配置。恢复先显式关闭 Core并等待 owner 释放，再由原恢复入口取得 profile/Core/数据库独占锁后替换数据，保留回滚副本。窗口取消等待不等于后台备份已经取消。

所有发行目标继续声明 AOT 兼容。桌面构建复制真正的 Core/Bridge 产物，publish 对宿主传递 RID 和 AOT 设置并收集 publish 输出；启动器支持普通 DLL 和原生宿主可执行文件。没有靠关闭项目 AOT 约束处理序列化，仍需要各平台实际 publish 和原生库验收。

## 使用和后续扩展

- 桌面 `npm run dev` 会构建宿主并自动连接或按需启动 Core。UI 退出后，活动任务和播放继续。
- 单独 Core：`dotnet run --project src/Echo.Core.Host -c Release -- --data <数据目录> --db <曲库文件> --native-root <音频宿主目录>`。
- 单独 Bridge：`dotnet run --project src/Echo.Bridge.Host -c Release -- --data <同一数据目录> --db <同一曲库文件>`。开发中可用 `ECHO_CORE_HOST` 指向对应 Core 产物；发行物按相邻 core/bridge 目录布局。
- CLI 的 scan/list/stats/issues/专辑元数据都附着同一 Core。显式临时数据库使用自己的数据目录，不与生产 profile 混用。
- 分离之后新增业务先定义 owner 和契约，再更新服务端能力清单及客户端；禁止在 Control 临时增加数据库后门。新输出端通过音频/会话端口接入，不新建第二套队列。

远程 Control 的配对/TLS 已接入，能力及验收边界见 [REMOTE_CONTROL.md](REMOTE_CONTROL.md)。网络 Output 已接入原生设备能力和有界二进制媒体链路，见 [NETWORK_OUTPUT.md](NETWORK_OUTPUT.md)；实机验收、跨机器时钟和分组同步仍需继续完成。不得将仅增加 TCP listener、复用 JSON 发送 PCM 或模拟宿主成功视为这些功能完成。

## 验证与集成状态

聚焦入口：`python3 tools/architecture/check_boundaries.py`、`dotnet run --project tests/Echo.Core.IpcSmoke -c Release -- .`。IpcSmoke 使用真实独立 Core/Bridge 进程、隔离 SQLite 与短 WAV；音频宿主是独立协议替身，不代表真实设备输出。覆盖共享队列、控制端断开后整批扫描、跨控制端提交、桥接权限、最大客户端准入、超大帧拒绝、Core 备份和重启恢复。

初轮进程检查发现 macOS Unix socket 名称超长，已缩短名称；随后端到端 IPC 检查通过。桌面基线有播客筛选条件表达式类型推断与侧栏可空性两处编译错误；隔离分支补上与主工作区同样的两行最小修复，未覆盖其余并行实现。

主工作区目前另有专辑资料 Core.Host/Core.Client/Core.Contracts、音频协议与多处 UI 的并行开发。这些同名项目尚未自动覆盖或合并；本批在 `codex/bridge-core-csharp` 隔离工作树交付，后续集成应合并能力，而不是让第二个 Core owner 常驻或覆盖已有改动。最终构建和短测结果在下方记录；真实界面、真实 DAC、跨机器音频和 AOT 发行物验收分别列为未完成项。


最终聚焦结果（2026-10-19）：

- 依赖边界脚本通过；静态 RPC 清单生成 106 个已批准操作；diff whitespace 检查通过。
- `Echo.slnx` Release 编译通过，0 错误、9 个既有 Avalonia XAML 警告，92.2 秒。使用单节点、关闭共享编译及 trim/AOT 分析器的命令行选项规避并行编译争用；没有修改项目 AOT 声明。此结果不证明 Native AOT 发行物已通过。
- 最终 IPC 冒烟检查通过，10.4 秒；使用真实独立 Core/Bridge 进程和 SQLite，原生音频为协议替身。覆盖上文列出的多控制端、最大客户端准入、任务断线存活、桥接能力限制、备份和持久化恢复。
- 真实 Avalonia 交互/外观、原生设备出声、Windows/Linux 实机、跨机器 Output、分组同步、AOT 发布及进程树性能测量未验收。后续新增远程 Control 与网络 Output V1 进展分别见 [REMOTE_CONTROL.md](REMOTE_CONTROL.md) 和 [NETWORK_OUTPUT.md](NETWORK_OUTPUT.md)，仍不能称为已经完成真实设备分布式输出验收。

- `tools/Echo.Preview` 的聚焦编译被既有 `SongDropSmoke.DropFile` 对 Avalonia 12 不再允许用户实现的 `IStorageFile/IStorageItem` 替身阻挡（CS0535）；未扩大修改无关拖放测试。三个使用旧默认组合入口的预览调用改为显式注入渲染 fixture，未宣称预览运行验收完成。


2026-10-19 深入输出端：新增 `Echo.Output.Contracts`、`Echo.Output.Client`、`Echo.Output.Host`。Core 组合入口可显式选择远端 `INativeAudio`，原生处理与设备留在 Output 机器；控制/媒体角色分别配对。机器本地绑定在自动 Core 启动时恢复，退出/崩溃释放原生租约。详细边界、预算、部署和未验收项见 [NETWORK_OUTPUT.md](NETWORK_OUTPUT.md)。
