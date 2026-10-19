# 原生音频引擎对齐与控制通道优化

2026-10-19。只修改 echocsharp；echosteam 作为只读源码来源。

C# 启动、队列、输出设置、DSP 界面和旧配置恢复的后续适配见 [NATIVE_HOST_ADAPTATION.md](NATIVE_HOST_ADAPTATION.md)。

## 对齐基线

以 ECHOSteam 已提交版本 `a44881fcc21a5d374d3bbd42aba1263b7ec71a28` 为基线，导入 `native/audio-engine` 和 `native/audio-host` 的全部 369 个 Git 文件，不读取其未提交内容。逐文件来源与本地哈希见 `native/AUDIO_ENGINE_UPSTREAM.json`。哈希统一 CRLF/LF，避免平台换行差异；原始历史导入记录 `IMPORT_MANIFEST.json` 继续保留。

原生 DSP、解码、SRC、SDM、PCM/DSD、设备后端、音频传输及对应原生工具与该提交一致。包括多段压缩、De-esser、虚拟声场、BS2B 交叉馈送和平滑切换、独立耳机校正、滤波机架、DSP 预构建与交接、SDM 健康监控、设备音量及 E-AC-3 输出实现。源码存在不代表每个平台或设备已通过验收。

唯一导入覆盖项是宿主 `CMakeLists.txt` 末尾加载本地 `cmake/EchoTrackAnalysis.cmake`，保留 C# 曲库现有的曲目分析工具和检查目标。现有 C# 专属文件保留；没有替换旧 Windows 运行库或第三方授权声明。

```sh
python3 scripts/sync-audio-engine.py --check
python3 scripts/sync-audio-engine.py --source /path/to/echosteam --revision <commit>
```

同步工具只读来源仓库的 Git 对象。再次导入前检查上次清单中的本地文件；出现本地差异时停止覆盖，先人工合并。清单检查负责源码来源一致性，不替代构建或设备验证。

## 运行边界

原生宿主继续独立进程运行，持有解码、DSP、音频缓冲、输出设备、时钟和 EOF 事实。没有把 PCM 引入托管控制通道，也没有向桌面增加音频处理或队列 owner。此次没有改变现有 Core/Control 迁移状态。

C# 播放命令 lane 新增多段压缩、De-esser、虚拟声场、耳机校正及 Workshop 效果的明确 DSP 方法，保留原生 Pro 权限检查、串行控制与恢复记录。耳机校正沿用原生完整 profile 契约，不把部分参数近似成完整校正。ready 消息的 `HostIdentity` 可用于检查实际运行宿主，重建设备进程时清除旧身份。

本次完成的是原生源码对齐和既有 C# 播放链路接入，不代表所有高级能力均已有桌面编辑器，也不代表跨机器输出、分组同步或桥核分离已经完成。

## 性能与资源

- 请求保持 UTF-8，去掉整条消息的 UTF-16 字符串及再次编码。
- 进入唯一 writer 后才序列化；最多一份待发送缓冲。发送结束即释放缓冲，不随等待回执的请求继续持有。64 个请求的准入改为原子计数，避免并发越过数量限制。
- 原生 JSON 响应直接按 UTF-8 分片解析，去掉逐行字符串和再次编码。接收缓冲按需增长，上限 256 KiB 加一个换行字节；已扫描的前缀不重复扫描。跨异步边界仍显式克隆 JSON，不能保留指向复用缓冲的视图。
- 丢弃的 stderr 直接用 4 KiB 缓冲排空，不解码、不逐行分配、不保留历史。设备发现的 stderr 使用同一路径。
- 不引入新的常驻进程、大对象池、预分配 PCM、自动重试或高频轮询；不缩小原生音频缓冲冒险换取数字。

同机同构建的短对比，预热后连续 128 次实际 `eq.getState`：优化前托管总分配 4,185,200 字节，优化后 2,390,192 字节，减少约 43%；耗时分别约 1559/1555 ms，基本持平。日志在本地忽略目录 `misc/audio-rpc-budget/`。这是 C# 控制路径分配比较，不是整棵进程树驻留内存、DSP 吞吐或播放时延的提升结论。

## 构建和验证

macOS/Linux 开发构建：`bash scripts/build-native-audio.sh`，需要 CMake、Ninja、C++20 和本机 FFmpeg 开发库。Windows 继续使用 `scripts/build-native-audio.ps1 -FfmpegRoot ...`。脚本将宿主放到对应 `native/runtime/<rid>`；Desktop 按目标 RID 复制 macOS/Linux 宿主及构建回执。Mac 本地开发依赖 Homebrew FFmpeg；正式发行的依赖打包、签名与公证另行完成，不能直接当作可分发安装包。

已验证：

- 当前 Apple Silicon Release 原生宿主、曲目分析工具及定向交叉馈送/EQ 目标构建成功，约 23 秒。
- 交叉馈送在 8/48/384 kHz 的启停、参数和算法切换无突跳、无处理期分配；双精度 EQ 的级联、多声道、作用域和旁路检查通过。
- 真实 C# 客户端读取宿主身份，新 DSP 状态/配置获得原生回执；静音 WAV 的设备输出、暂停、seek、自动推进、插队 FIFO、EOF、宿主崩溃恢复、checkpoint 和释放通过。
- UTF-8 分片、连续消息、JSON 克隆所有权、256 KiB 边界及残缺消息拒绝使用聚焦检查。
- Desktop 开发构建成功；已有 Avalonia 资源加载/旧属性警告保留。

Windows/Linux 构建、真实 ASIO/KS/DSD/HDMI/CUDA、听感、长时间播放、AOT 发布及整棵进程树 CPU/驻留内存对照未在本轮验收。没有运行全量 native 测试、长测或打包。

DSP 控制层本轮覆盖与剩余差异见 [DSP_PARITY.md](DSP_PARITY.md)。
