# 原生 PCM 数据面：第一阶段

更新：2026-10-19。新增的是可编译、可运行的原生解码发送入口和原生输入适配，尚未接入托管 Core/Output 默认播放组合。现有网络 Output V1 仍是原始文件转发，不能视为已达到新的原生处理端／输出端分工。

Roon 官方明确说明 Server 读取音源、解码成 PCM/DSD，再发送到 Output；Bridge 将本机音频设备暴露为端点。参考其[架构职责](https://help.roonlabs.com/portal/en/kb/articles/architecture)和 [Bridge 部署边界](https://help.roonlabs.com/portal/en/kb/articles/roonbridge)，不推断或实现 RAAT 私有协议。

## 本批实现

```text
Core 机器的 Native Audio Host
  DecodePcmCommand：原生文件读取、现有 libav 解码
       │ PcmWireProtocol::Writer：EPCM 二进制流
       │ 专用原生管道描述符，无 JSON PCM、无托管字节转发
       ▼
Output 机器的 Native Audio Host（管道接入代码；网络部署尚未连接）
  RawPcmInputReader → PcmWireProtocol::Reader
       → generation 检查 → 现有 NativePlaybackPipeline → 设备回调
```

图中跨机器连接尚未实现。现阶段验证通过的是同机原生字节管道，不能用两端已有代码推断跨机器可用。当前接收仍进入既有包含 DSP 的播放管线，Core 侧 DSP 编排和 Output 纯输出路径尚未拆开。

- `PcmWireProtocol` 独立拥有字节格式、解析状态和一包缓冲；不依赖 UI、业务数据库、网络库或 JSON。
- `DecodePcmCommand` 从超长 `main.cpp` 抽出原有解码命令。未启用新参数时保持原始 PCM 输出及既有解码行为。
- `PcmWireInputBinding` 负责输入配置和原生代次绑定。宿主入口只组合模块。
- 解码 EOF 只发送 `End`；接收端完成全部 PCM 入环后才标记输入结束。它不构造设备 drain、播放成功或业务队列推进事实。
- `NativePlaybackPipeline.markInputEnded(expectedGeneration)` 在处理锁内先核对代次，再 flush DSP；延迟到达的旧 End 不得 flush 或结束新会话，保留既有提交尾部时的代次检查。
- 格式、序号、采样位置、包长或代次错误，以及无 End 的 EOF，均终止该输入。宿主停止对应原生代次并通过控制通道发 `audio.pcmTransportError`（epoch/message），不把断线当自然曲终。

## EPCM v1 格式与预算

所有整数字段为 little endian。PCM 为 IEEE 754 float32 little endian、交错声道。只接受有限值；当前 1–8 声道、8–768 kHz，实际设备能力仍需独立协商。

| 偏移 | 字段 |
| --- | --- |
| 0 | magic `EPCM`，4 字节 |
| 4 / 6 | version u16=1 / kind u16：Begin=1、Data=2、End=3 |
| 8 / 16 / 24 | epoch u64 / 连续 sequence u64 / 当前会话起点之后的 sample-frame position u64 |
| 32 / 36 / 38 | rate u32 / channels u16 / format u16=1 |
| 40 / 44 | frames u32 / payload bytes u32，严格等于 frames×channels×4 |
| 48 / 56 | 发送端单调时钟 ns u64 / 保留字段 u64=0 |

头固定 64 字节；负载上限 64 KiB。Begin/End 无负载；Begin 序号为零，Data/End 连续递增，位置逐包连续；不得重复 Begin、End 后继续写或溢出计数。epoch 由编排方指定非零 u64，接收方必须预先知道相同值，不能从到达的数据自行切换会话。

Reader 只预分配一包 64 KiB，输入工作线程另有 64 KiB 读缓冲；framed 模式不再预留旧 raw 模式的 pending 缓冲或逐包分配 float 数组。逐包同步调用原生有界环，环满时停止解析和读取，管道回压到发送工作线程；音频回调只消费原有环，不做网络、序列化或阻塞写。发送端复用现有每次最多 4096 帧的解码块，按包长上限切分，不保留整曲 PCM。

发送时钟字段只提供后续同步的采样依据，当前接收器不根据它校正设备时钟，也不比较两机单调时钟绝对值。它不意味着已经实现漂移校正或同步播放。

## 原生入口和生命周期

发送原生进程示例（stdout 是二进制数据，日志留在 stderr）：

```sh
echo-audio-host -decode-pcm /path/to/track.flac -sr 48000 -ch 2 \
  --pcm-wire-epoch 42 > /private/track.epcm
```

`-sr` 和 `-ch` 必须与源匹配；现阶段不增加解码命令原本不支持的重采样/声道转换。`-ss` 沿用原生 seek，流内 position 重新从零计数；媒体位置起点应由未来控制契约单独携带。

接收进程由启动器提供专用 `--pcm-input-fd N --pcm-wire-epoch 42`，并提供不同的 `--rpc-stdin-fd`/`--rpc-stdout-fd`。数据 fd 不得复用控制 fd；拒绝 DoP、原生 DSD、listen-together 和解码输出混用。Windows 管道继续使用现有 `_read/PeekNamedPipe` 实现；本批仅在 macOS 编译和运行检查。

这是一次一会话的进程协议：先通过现有控制通道取得 `audio.sessionBegin(sessionId=1)` 成功，再投递 Begin。Reader 绑定该原生代次；后续更换会话不重新绑定旧流，seek/切歌需新进程/epoch。关闭时沿用先 requestStop 解除环背压、再 stop/join 输入线程的顺序。发送进程如阻塞于管道写，由拥有它的启动器有界终止；当前没有网络传输超时能力。

这条入口尚不由 `Echo.Output.Host` 或 `RemoteNativeAudioHost` 创建，也没有对现有 V1 自动切换或降级。不要直接把它当现有网络配对文件的替代。

## 聚焦验证与未完成项

```sh
cmake -S native/audio-host -B out/native-pcm-wire \
  -DECHO_ENABLE_CUDA_DSP=OFF -DCMAKE_BUILD_TYPE=Release
cmake --build out/native-pcm-wire --target echo-audio-host echo-pcm-wire-tests -j 3
ctest --test-dir out/native-pcm-wire -R '^echo-pcm-wire-tests$' --output-on-failure
```

macOS 原生宿主 Release 编译成功。聚焦测试 0.64 秒通过：临时生成 48 kHz 双声道 WAV，由真实 libav 解码，经多包/碎片输入还原，比较与旧 raw 解码逐字节相同；拒绝旧 epoch、乱序、格式错误、超大负载、NaN、截断流及停止的输出；真实 OS 管道检查同步背压和 End 顺序。另用真实宿主 CLI 检查最大 u64 epoch、包长/序号/位置，以及数据/控制 fd 混用拒绝。测试未打开音频设备。

尚需：原生到原生鉴权和加密网络传输、取消/超时、动态会话代次与控制回执、Core 原生 DSP/处理端、Output 纯输出适配、格式与时钟协商/校正、队列及源能力接入，并切换默认生产组合。之后再验证双机、DAC、断连/重连、Control 关闭、跨平台发布和进程树资源。仅在本批完成的边界运行检查，不重复托管端全量测试，也不宣称桥核分离完成。
