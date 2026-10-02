# Windows SMTC 迁移

2026-10-02。Windows 系统媒体控制已接入 Avalonia 桌面实际装配入口。

## 职责与调用链

- `DesktopComposition` 装配并持有生命周期，Windows 首次有曲目时才启动 helper。
- `PlaybackMediaTransport` 订阅唯一播放 owner 的确认快照，将系统命令提交到同一个会话命令通道。
- `MediaTransportMapping` 生成有界 DTO；不读取磁盘，不持有整库，不创建 UI 播放时钟。
- `Echo.Platform/Windows` 管 JSONL 协议、能力握手、进程、状态发布和错误通知。业务层不引用平台 API。
- `native/smtc-host` 复用旧 helper；随当前应用携带本地 Windows x64 binary，不在运行时引用旧仓库。

已接入：标题、艺术家、专辑、专辑艺术家、本地封面路径、播放状态、确认进度、播放/暂停/停止、上一首/下一首、seek 和循环模式。停止后系统播放键会通过现有队列重新打开当前曲目。队列为空时清除系统会话；队列更新会发布新的动作能力。

信息来自当前 `QueueTrack.Metadata`：`title`、`artist`、`album`、`albumArtist`、`coverPath`。
缺少标题时使用文件名；没有可用的绝对封面路径时清除封面。
当前普通文件打开入口没有完整标签和封面读取器，因此不能宣称已自动抽取嵌入封面或所有标签。
远程封面缓存、SMTC 歌词和随机播放没有迁入；目前播放层没有对应 owner，随机请求会回写实际关闭状态。
macOS/Linux 此轮不装配 Windows helper，系统集成尚未迁移。

## 资源、取消、释放和恢复

状态保留一个最新 DTO；待处理系统命令最多 16 条，满时拒收新增请求。
状态文本每项最多 2048 字符；输入行最多 16 Ki 字符；写入最多 2 秒；握手最多 2.5 秒。
发布最高约 10 Hz；封面/标签只在变化时更新，避免每个音频 tick 重开图片。
一个 helper、两个有界管道读取缓冲，不保存诊断历史。重复错误不持续通知 UI。
helper 启动或连接失败只关闭系统集成，在桌面展示“重试系统媒体控制”。重试会重新握手并发布最新状态，不重放旧命令。

关闭窗口先取消未开始的系统命令、解除订阅，再发送 dispose。helper 500 ms 内未退出时只终止本适配器创建的进程树。
已经发给音频 owner 的命令仍依照既有确认规则收口，之后释放音频会话。
主窗口最小化不停止系统媒体控制。

开发覆盖：`ECHO_SMTC_HOST` 可指定 helper；`ECHO_SMTC_ENABLED=0` 可在下次启动时关闭本能力。
默认使用输出目录 `native/echo-smtc-host.exe`。旧应用保留作回滚，不修改旧音频或生产库。
原生来源及 binary SHA256 见 `native/smtc-host/ORIGIN.md`；本轮没有重编 C++、做 AOT 发布或完整打包。

## 短验证证据

`tests/Echo.MediaTransport.Smoke` 覆盖映射、状态转换、命令路由、取消/解绑、队列清空、helper 缺失与显式重试。
`Windows-Readback.ps1` 使用 Windows 系统会话 API 读回媒体信息、可打开的本地封面流、播放状态和 timeline，并从系统会话发暂停、seek、下一首。
使用本地静音 WAV 与当前 Native Audio Host 短测：系统命令经 coordinator 到真实原生解码/输出确认通过。
测试封面使用已有 PNG 夹具；不代表歌曲封面自动抽取或系统浮层视觉验收。

运行方式（在工作区根目录，按约束加进程超时）：

```powershell
dotnet run --project tests/Echo.MediaTransport.Smoke -- native/runtime/win-x64/echo-smtc-host.exe tests/Echo.MediaTransport.Smoke/Windows-Readback.ps1 native/runtime/win-x64/echo-audio-host.exe
```

此轮只做短编译和以上定向检查。未验收媒体键硬件、锁屏视觉、睡眠恢复、长期泄漏、三端集成或完整发行。

SMTC/Playback/Platform 定向编译通过，桌面装配曾在本轮中间快照编译通过。
最后一次默认桌面构建受并行新增的 `MotionFeedback`、`MotionSurface`、`MotionFlyout` 编译错误阻塞；
这些文件不属于本次迁移，未改动它们。不能把中间快照的编译结果当作当前整个工作区已通过。
