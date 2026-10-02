# 播放链路迁移

日期：2026-10-02。当前迁移仅修改新工作区，保留旧 ECHO。

专业输出后续接入：ASIO、WASAPI 独占、WDM-KS、多声道初始化、驱动失败隔离和 DSD 能力探测见 [PROFESSIONAL_OUTPUT.md](PROFESSIONAL_OUTPUT.md)。当前设置已扩展，下面的首版说明保留其验证时间边界。

## 已接入

`Avalonia PlayerBar → PlayerViewModel → PlaybackSessionCoordinator → 有界 JSON-RPC → C++ Native Audio Host → libav 解码 / 原生 PCM ring / DSP / 设备输出`。

- C++ `native/audio-host`、`native/audio-engine` 保留完整源码、原生测试和 vendor 文件；不把解码、重采样、SDM/DSD、设备回调或 PCM 复制进 C#。
- 普通本地文件、直接 HTTP 音频源、显式 CUE/片段 source descriptor 由原生宿主解码。播放栏支持多选文件、播放/暂停、上一首/下一首、seek、音量、队列双击和循环模式。
- 宿主拥有自动推进、EOF、drain 和原生时钟。C# 以 operationId、fromOperationId、itemId 和 queueRevision 接受事件；不通过界面计时器推算结束或重复开下一首。
- DSP 控制入口覆盖 EQ、DSP rack、ReplayGain、压缩、交叉馈送、声场、矩阵、卷积、速度、预设与 profile；宿主仍执行原有能力/Pro gate。首版设置入口提供输出设备、shared/exclusive、EQ、交叉馈送、速度；其他原生 DSP 的专用编辑界面尚待迁移。
- 明确重试会重新建立失败宿主，恢复输出配置及有界 DSP 命令，再从最后原生位置打开歌曲。
- 新应用独立 `ECHO Avalonia/playback.sqlite` 保存队列、位置、音量、输出和 DSP 控制状态。只在启动恢复和正常关闭保存；恢复为暂停，不自动出声。凭据型 HTTP 源不落盘。数据库独立于曲库，只有一个 SQLite lane 和 writer lock。

## 资源与生命周期

控制命令 64 项；RPC 待处理最多 64；单条 RPC 256 KiB；队列窗口 1000 项；DSP 恢复记录最多 128 项；状态文件读取/保存最多 512 KiB。位置事件合并，UI 只发布最新快照。取消在命令准入时生效；已发送的原生写操作等待确认或失败，不能假装撤销。退出先保存状态，再释放系统媒体控制和自有音频进程。

原生 ready 超时 12 秒，RPC 10 秒；退出先请求 shutdown，再终止本模块启动的自有进程。宿主失败和设备错误有明确状态及重试入口。没有改动旧 ECHO 生产曲库。

## 运行

Windows：`dotnet run --project src/Echo.Desktop`，播放栏“＋”选择音频文件。

独立入口：`dotnet run --project tools/Echo.Player -- <host> <audio-file> [audio-file ...]`。命令包含 pause/play/seek/volume/next/prev/repeat/devices/recover/quit。

宿主默认从应用输出目录的 `native/` 加载，可用 `ECHO_AUDIO_HOST` 指向本平台构建产物。Windows 本地开发运行库已导入；macOS/Linux 使用相同 C++ 源码及各平台构建产物，不能使用 Windows DLL。

Windows 原生重建：`scripts/build-native-audio.ps1 -FfmpegRoot <FFmpeg-development-root>`；`-CpuOnly` 禁用可选 CUDA。配置需要 CMake、C++20 编译器和对应 FFmpeg 开发包。此次没有运行耗时完整 C++ 编译或打包。

## 验证与边界

最小 smoke 使用新建无声 WAV 与真实 Windows 音频设备，验证 libav 解码、播放输出确认、暂停、seek/resume、自动切歌、drain/end 和释放；另外覆盖陈旧事件、队列版本、DSP 控制、进程恢复与 checkpoint。实际结果见本轮命令输出。

本轮原生功能 smoke 通过，音频部分约 2.16 秒；Avalonia 播放栏构造/真实状态绑定、独立 SQLite 状态往返和 Headless 渲染通过。截图位于 `misc/playback-ui-smoke/player-paused.png`，展示无声 WAV 暂停时的实际控件渲染；这不是完整桌面点击/设备听感验收。构建使用导入的原生运行库，未重编整套 C++。

这是可运行的基础播放链路。CUE 文件列表解析、Audio CD 的专门读取服务、Automix 策略规划、远程来源业务/权限及完整 DSP 编辑界面尚未与新 UI 对齐；相关 C++ 底层源码已保留。ASIO、KS、多声道、USB/DSD、CUDA、跨平台设备及长时间无缝/高倍率压力没有进行硬件验收。无声 WAV 的设备输出确认不等于听感验收。

源码来自旧 ECHO 工作区，导入时 HEAD 为 `3dbf28993cddf9f10ea355b88b0105abe85e5174`，包含该工作区当时内容；源码/运行库哈希见 `native/IMPORT_MANIFEST.json`。源码保留 ECHO AGPL-3.0 许可证与原有第三方头文件声明。运行库用于本地开发验证；发行时仍须按平台完成 FFmpeg、ASIO、其他 vendor 的 notices、再分发和产物检查。
