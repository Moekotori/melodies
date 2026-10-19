# 独立网络 Output V1

2026-10-19。新增 `Echo.Output.Host`，负责设备所在机器的原生音频进程、设备能力、媒体接入及播放事实回报；Core 通过 `INativeAudio` 使用它。已有 `Echo.Bridge.Host` 继续承担 DLNA/Link/MQTT 等协议网关职责。

```text
Control ── 已有 IPC / 配对 TLS ── Core.Host
                                  ├─ 唯一曲库、队列策略、任务、checkpoint
                                  ├─ Output.Client ── 配对 TLS 控制 ── Output.Host
                                  │                                  └─ 独立 Native Audio Host
                                  └─ 有范围的只读媒体服务 ── TLS ── 回环 HTTP 代理 ── 原生解码/DSP/设备
```

这是按原始音频文件字节拉取的网络输出路径。未对解码 PCM 做 JSON 序列化，也不在 Control 创建音频缓冲；Core/Output 的托管媒体服务只流式搬运文件字节，WAV 容器也按文件字节处理。设备所在机器的原生宿主继续完成解码、有界 PCM、DSP、时钟和设备输出。没有更改 C/C++ 音频实现或新增第三方生产依赖。

## 运行边界

- `Echo.Output.Contracts`：版本、初始化能力、原生命令/事件、媒体能力及静态 JSON 上下文，只引用音频契约。
- `Echo.Output.Client`：Core 的远程 `INativeAudio/INativeOutputControl` 适配、Core 源身份投影、只读媒体能力与字节流服务；不引用 Playback/Core 业务实现或本地 Audio.Client。
- `Echo.Output.Host`：单 Core 租约、原生进程、设备枚举、回环媒体代理与事实回报；不引用 Core 实现、Core.Client、SQLite/Infrastructure、Playback 实现或 UI。
- 复用 `Echo.Core.Transport` 的有界信封、TLS、配对与连接生命周期。信封 JSON 上下文与业务 RPC 上下文分开，减少 Output 对曲库 JSON 模型的根引用。尚未用 AOT 发布和内存测量证明具体收益。
- 默认本地输出继续直接使用独立 Native Audio Host，不为每个本机用户额外启动一个 Output 托管服务。需要分别部署时显式选择网络 Output。

Core 产生队列策略、版本与执行计划。Output 不打开业务数据库、不扫描、不保存业务队列或刷新音源凭据；Native 沿用已有执行计划完成衔接并回报事实。播放成功、暂停、恢复、位置、EOF/切换与设备配置均来自原生协议确认，控制端与输出网关不生成成功事件。

## 双向配对与部署

示例 Core IP 为 `192.168.1.10`，Output IP 为 `192.168.1.30`。替换为实际地址及已存在的私密文件目录；配对命令里的 data/db 必须与运行时一致。两台机器都需要对应平台的 .NET 或 Native AOT 发布物，Output 机器需要匹配的原生宿主及动态库。

1. 在 Core 机器签发**只读媒体**配对，记录输出的客户端 ID：

```sh
dotnet src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll \
  --data /path/to/core-data --db /path/to/core-data/library.sqlite \
  --pair-output-media /private/output-media.echo-core-client.json \
  --remote-host 192.168.1.10 --remote-port 42832
```

2. 在 Output 机器签发**音频控制**配对：

```sh
dotnet src/Echo.Output.Host/bin/Release/net10.0/Echo.Output.Host.dll \
  --data /path/to/output-data \
  --pair-controller /private/core-controller.echo-core-client.json \
  --host 192.168.1.30 --port 42833
```

私密传送 `output-media.echo-core-client.json` 到 Output，传送 `core-controller.echo-core-client.json` 到 Core。文件含随机访问密钥，不能提交或公开分享。访问密钥不放在启动参数中。

3. 启动 Output：

```sh
dotnet src/Echo.Output.Host/bin/Release/net10.0/Echo.Output.Host.dll \
  --data /path/to/output-data --listen 192.168.1.30:42833 \
  --native-host /path/to/native/echo-audio-host \
  --media-connection /private/output-media.echo-core-client.json
```

4. 启动 Core，将第 1 步的媒体客户端 ID 填入：

```sh
dotnet src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll \
  --data /path/to/core-data --db /path/to/core-data/library.sqlite \
  --output-connection /private/core-controller.echo-core-client.json \
  --output-media-listen 192.168.1.10:42832 \
  --output-media-client-id MEDIA_CLIENT_ID
```

这份绑定作为机器本地部署配置保存在 `core-output.json`，只记录配对文件路径、监听地址和客户端 ID，不复制访问密钥。以后由 Control 自动启动 Core 也会恢复相同输出；配对文件缺失、过期或错误时明确失败，不能静默播放到 Core 本机。控制端显示 `网络 Output` 区域名。

切回本机输出需在 Core owner 停止后显式启动 `Core.Host ... --local-output 1`，清除该绑定。输出绑定和配对不随业务备份迁移，恢复曲库不会自动授权另一台机器。变更绑定时先停止旧 Core；不能通过第二个进程改写活跃 owner 的配置。

Control 连接 Core 的方法保持不变；需要远程 Control 时仍使用单独的 `remote-control` 配对和 `--remote-listen`，见 [REMOTE_CONTROL.md](REMOTE_CONTROL.md)。

## 权限和生命周期

| 边界 | 行为 |
| --- | --- |
| 三种配对角色 | `remote-control`、`output-controller`、`output-media` 独立授予，服务入口核对角色及哈希，修改客户端文件中的角色不能提权 |
| Output 控制 | TLS 指纹/期限校验；同一时刻一个 Core 租约；第二个 Core 被拒绝，不接管现有音频 |
| 原生操作 | 明确白名单；打开/执行队列只接受当前回环代理内已发布的媒体能力；禁止直接读取 Output 本机路径和任意 HTTP URL |
| 媒体读取 | TLS + 指定媒体客户端 ID + 随机文件能力 + offset/count；请求中没有文件路径，不提供目录遍历或任意文件 API |
| 文件变化 | 打开范围时核对长度与修改时间；已变化的文件读取失败，不伪造完整传输 |
| 输出代次 | 服务 instance、连接租约、原生宿主 generation、operationId 各有边界；晚到事实与旧代次命令被拒绝 |
| 遥测 | position/performance/level 只保留最新值，按事件唤醒并合并约 100ms；不含媒体地址的遥测直接复用 JsonElement，避免重建 JSON DOM；空闲不持续以视觉帧率轮询 |
| 结构事实 | EOF/切换/错误等走有界可靠通知；队列满时使连接可观察地失败，不静默丢失事实 |
| Core 消失 | TCP 断开立即回收租约与原生进程；半开连接在 15 秒无联系后触发断开（检查间隔最多约 1 秒）；设备确认只在新会话中恢复 |
| 释放 Control | 只断开 Control；Core、Output 和活动音频继续；Core 正常或异常退出才触发 Output 原生释放 |

Core 控制连接每 5 秒有界健康探测，普通请求不会重放到重连后的新连接。Output 不自动接管或重播故障前的音频；Core 重启恢复 checkpoint 后等待明确播放请求（用户已开启的 Core 自动播放策略单独适用）。

撤销输出控制配对：在 Output 机器运行 `Output.Host --data ... --revoke-controller CLIENT_ID`。撤销媒体配对：在 Core 机器运行 `Core.Host --data ... --db ... --revoke-client MEDIA_CLIENT_ID`。控制撤销会断开当前租约并释放原生进程；媒体撤销在有界刷新后停止后续字节传输。需要重新配对时更新双方文件和 Core 绑定。

## 资源预算

- 控制通道沿用最多 8 个连接及有界 pending、握手、帧和写入期限；Output 额外只授予一个执行租约。原生控制请求仍受既有 256 KiB 上限约束，URI 投影后超限明确失败；V1 尚未加入短执行窗口优化。
- 媒体服务和回环代理分别最多 4 路请求；每路 64 KiB 流式缓冲，不预读整首歌曲。单个流按 10 秒无进展期限退出，长文件可以持续流式读取。
- Core 媒体能力最多 1024 条、累计路径字符 262144；只保留路径和文件事实，不为整队列打开文件句柄。回环代理同样最多 1024 条能力，仅持有 ID/长度/扩展名。
- 媒体握手 4 KiB，回环 HTTP 头 8 KiB/64 行；支持 GET/HEAD 和单一字节 Range，校验回环 Host，连接完成关闭。
- 配对表维持原有 16 项/32 KiB 总预算，文件 Unix 0600 / Windows 当前用户 ACL；不在日志保留原生 stderr 或能力、访问密钥；回报中的回环媒体 URL 还原为 Core 源身份，嵌入错误文本的能力地址被替换。

这些是静态上限，不是已测得的 CPU、驻留内存或设备延迟。受限能力淘汰/源文件变更/超时应当显式失败，不能回退任意路径或无界缓存。

## V1 范围与验收

当前实现单 Zone、完整 Core 本地音频文件的原生 PCM 输出、设备枚举及现有播放/音量/位置控制。CUE 时间范围、组合/混音源、网盘和直播源、DSD 输出/探测、文件型或未列出的 DSP 操作明确拒绝；不存在自动格式或本机路径降级。安全白名单中的常规控制仍以原生返回为准。真实 gapless、音频 seek、格式覆盖和驱动行为需要实机验收。

`tests/Echo.NetworkOutput.Smoke` 运行独立 Core、Output、音频协议替身进程，经真实 TCP/TLS 将超过 1 MiB 的 WAV 文件流式传到原生侧替身，校验完整 SHA-256 和 Range，检查设备枚举、播放/暂停/恢复、源身份、角色/文件能力拒绝、单 Core 租约、Control 断开、Core 崩溃后原生回收，以及无额外参数重启 Core 后输出绑定保留。本检查不修改既有 `Echo.Output.Smoke` 原生设备测试。

这证明协议、字节流和生命周期，不证明真实解码或出声。本检出没有可用于本次 macOS 出声验收的音频宿主，使用协议替身；尚未验收真实 DAC、两台物理机器、实际 LAN/防火墙、Windows/Linux、Native AOT 发布、时钟漂移/同步或进程树性能。没有 SSH、服务器部署或系统服务安装。多 Zone 分组、采样时钟同步与 RAAT 兼容不在该版本内。


本次聚焦验证：Release 解决方案编译通过，14.8 秒、0 错误、9 个既有 Avalonia 警告；网络 Output 冒烟 5.8 秒、远程 Control 冒烟 4.4 秒、本机 IPC 冒烟 3.8 秒均通过。构建使用单节点并在命令行关闭 trim/AOT 和普通分析器，生产项目的 AOT 声明未改变。依赖守卫与 diff whitespace 检查通过。没有扩大到全量设备长测。

最后的媒体投影快速路径增量编译通过（3.5 秒，0 错误/警告），网络 Output 同范围冒烟再次通过（5.2 秒）；未扩大其他测试。
