# 远程 Control

2026-10-19。远程桌面/CLI 现在可以通过配对的 TLS 连接使用同一个 Core；默认音频在 Core 所在机器的原生宿主输出；可显式绑定 [独立网络 Output](NETWORK_OUTPUT.md)。此功能不等于远程音频 Output，也不提供 RAAT 兼容或多 Zone 同步。

## 所有者启动与配对

默认启动仅监听当前用户本地 IPC。网络监听必须由 Core 所有者显式启用。以下路径均为示例，数据目录应与实际曲库一致，配对文件的父目录必须已存在。

先在 Core 机器签发一个控制端文件：

```sh
dotnet src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll \
  --data /path/to/echo-data --db /path/to/echo-data/library.sqlite \
  --pair-client /private/transfer/laptop.echo-core-client.json \
  --remote-host 192.168.1.20 --remote-port 42831
```

此命令只签发配对，不启动 Core，不连接网络。输出客户端 ID，不输出访问密钥。用私密方式把该文件交给指定控制端。该文件含访问密钥，不能提交 Git、公开分享或写入日志。

启动同一个 Core owner：

```sh
dotnet src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll \
  --data /path/to/echo-data --db /path/to/echo-data/library.sqlite \
  --native-root /path/to/native --remote-listen 192.168.1.20:42831
```

`--remote-host` 是控制端能访问的地址；`--remote-listen` 是 Core 本机实际绑定的 IP 和端口，支持带方括号的 IPv6 地址。不会自动开放防火墙、映射公网端口或安装系统服务。显式开启网络监听且仍有有效配对时，Core 保持可接入，直到所有者关闭；撤销全部配对后才恢复普通空闲退出规则，避免关闭控制端后远程机器再也无法连接。若 Core 已在运行，应停止原 owner 后以新监听参数重启；第二个 Core 不会偷偷改动运行中的监听器。

控制端桌面：

```sh
ECHO_CORE_CONNECTION_FILE=/private/echo/laptop.echo-core-client.json dotnet src/Echo.Desktop/bin/Release/net10.0/Echo.Desktop.dll
```

CLI 也可显式选择：

```sh
dotnet src/Echo.Cli/bin/Release/net10.0/Echo.Cli.dll list \
  --connection /private/echo/laptop.echo-core-client.json --limit 100
```

`--connection` 与 `--db` 互斥。配对失效、证书变更或网络失败时直接报错，不会启动本机 Core 或打开本机数据库作为回退。客户端只携带配对文件路径，访问密钥不放在命令行参数中。

撤销指定客户端，在 Core 机器运行：

```sh
dotnet src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll \
  --data /path/to/echo-data --db /path/to/echo-data/library.sqlite \
  --revoke-client CLIENT_ID
```

运行中的 Core 每秒重新读取有界配对表；撤销或过期后断开对应现有连接并拒绝重新握手。本地 Control 和其他已配对客户端继续使用同一个 Core。重新签发会创建新的 ID；默认有效期 90 天，最多保留 16 个未过期配对。证书有效期一年，证书到期/轮换后需要重新配对，没有自动跳过证书校验的选项。

## 权限与数据边界

| 能力 | 远程 Control |
| --- | --- |
| 分页曲库、专辑/艺人/流派/目录、已有歌单和历史浏览 | 允许列出的查询，新增 RPC 不自动开放 |
| 入库曲目的播放、排队、暂停、seek、音量、队列编辑 | 允许；Core 从入库记录重建音源，不采用客户端自带的路径覆盖、HTTP headers、CUE range 或元数据 |
| 收藏与专辑个人状态 | 允许 |
| 封面、歌词及目录说明 | 仅已入库曲目；禁止显式歌词路径和任意附件读取 |
| 音源凭据、连接配置、原生 DSP/设备配置、备份恢复、扫描/维护、Bridge 管理、关闭 Core | 不授予 |
| 任意本机文件、任意 HTTP 地址、raw SQL、通用方法调用 | 不提供 |

这是浏览与播放控制能力，不是远程管理员界面的完整功能对等。桌面中暂未支持的管理动作会返回权限/不支持错误。远程控制窗口不会恢复本机偏好到 Core 音频设置，不会把 Core 路径交给本机默认文件打开器，或创建本机 Bridge/数据库。原生设备切换和 DSP 仍由 Core 本机的受信任控制端管理。

普通状态投影删除音源对象及 HTTP headers、流 URL、内部输出/DSP 配置与诊断；保留有界队列及展示元数据。Core 为远程控制端返回已入库曲目的安全音源描述；远程队列请求在服务端重新核对入库记录，不信任客户端回传的描述。后台任务和持久化所有权与本地 IPC 一致。

## 传输与生命周期

- 采用 [.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0) 的 TLS 1.2/1.3；控制端严格校验配对文件中的证书 SHA-256 指纹和证书有效期。信任由所有者发放的文件建立，不从未验证的网络发现结果自动信任证书。
- 配对访问密钥为随机 256 位，只在 TLS 建立后用于握手。Core 仅存密钥哈希，并用固定时间比较验证；远程 Control 入口只接受 `remote-control` 角色；输出控制和只读媒体分别使用独立角色，不能修改客户端文件中的角色提权。
- 配对文件 Unix 权限 0600，服务端目录 0700；Windows 写入时设置仅当前用户 ACL。macOS 使用平台支持的默认密钥导入方式；其他平台采用 EphemeralKeySet。证书对象随 owner 生命周期释放；没有自行持久导入客户端证书。
- 本地 IPC 与 TLS 合计最多 8 个活动连接，TLS 和应用握手共用 5 秒期限。每连接 pending、普通/播放控制请求预算、4 KiB 握手、1 MiB 请求、8 MiB 响应、5 秒写入期限及合并订阅沿用同一实现。
- 撤销表最多 16 项/32 KiB，读取失败即拒绝访问并断开现有远程连接；只在显式网络服务运行时启用每秒检查。
- 远程连接每 5 秒发送一个有 5 秒期限的轻量健康请求（繁忙时跳过），在网络黑洞时也能关闭失效连接并显示故障。断开使待处理请求失败并暴露连接故障。显式重连最多 3 次，不自动重放写命令；排队等待发送的请求绑定原连接，重连后不会转发到新连接；Core 实例代次检查继续生效。断开控制端不停止播放。
- 不同机器的 Stopwatch 不能直接相减。远程播放观察以本机收到帧的单调时间重新建立显示锚点，不把服务端计数当成本机时钟。网络延迟仍影响显示精度；没有承诺时钟同步或采样级精度。未同步的性能时间戳/电平不显示为实时测量。
- 配对密钥与服务端证书不进入业务备份；恢复备份不应自动授权另一台机器。

## 验证范围

聚焦入口：`tests/Echo.Core.RemoteSmoke`。使用独立 Core 进程、真实 TCP/TLS 和 SQLite，原生音频是独立协议替身。检查共享 owner、分页浏览、播放控制、音源重建、敏感状态过滤、凭据/维护/任意文件/关闭服务拒绝、错误指纹和密钥拒绝、在线撤销，以及撤销后本地连接仍可使用。

测试使用 loopback；不同物理机器、实际 LAN/防火墙、真实音频设备、Windows/Linux 权限行为、发行 AOT 与桌面实界面尚需独立验收。没有通过 SSH 部署或更改任何服务器。


本次聚焦结果：Release 解决方案编译通过（56.0 秒，0 错误、9 个既有 Avalonia 警告）；最终相关项目增量编译通过（7.1 秒，0 错误/警告），远程 TLS 冒烟通过（3.9 秒），原有本机 Core/Bridge IPC 冒烟通过（4.1 秒），依赖边界及 diff whitespace 检查通过。首轮未关闭 trim/AOT 分析器的 Core.Host 构建虽通过，但耗时 27 分钟；后续命令行关闭该分析及普通分析器，只做聚焦验证。项目 AOT 声明未改变，没有把编译当成 AOT 发布验收。健康探测、证书和权限策略的代码检查不替代真实网络故障、跨平台及设备验证。
