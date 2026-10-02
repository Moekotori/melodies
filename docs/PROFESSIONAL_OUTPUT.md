# 专业音频输出接入

2026-10-02。音频数据面继续使用 C++；C# 只发送配置和接受宿主事实。

## 已实现

- 依据真实 native ready capabilities 显示系统共享、WASAPI/CoreAudio 独占、ASIO、WDM-KS。模式切换后重新枚举该模式的设备，不把共享输出的 index 当作 ASIO driver index。
- 原生 `-list` / `-asio` / `-exclusive` / `-ks` 枚举经有界短进程适配；最多 256 个设备、1 MiB 响应、8 秒超时，只终止本次适配器启动的进程。
- 输出设置可选择设备、请求采样率、缓冲帧数。ASIO PCM 可设 2–8 声道和起始声道；界面从 1 起，传给 native 从 0 起。超过设备范围由宿主拒绝，不能声称多声道已成功。
- 声道布局变化通过 `--defer-channel-init` / `host.initialize` 重新建立原生宿主，保留队列、DSP 和当前进度。声道及模式切换不能只改 UI 枚举。
- ASIO/KS/独占模式切换隔离驱动关闭，通过自有宿主重建避免驱动阻塞 `device.configure`。失败会尝试恢复原来的输出、队列、进度及暂停状态，并明确报告回退。
- transport epoch 隔离不同宿主的迟到事件，即使 operationId 从 1 重新开始，也不能采纳旧进程的 EOF/时钟。
- 专业输出分别等待解码 PCM 就绪和实际输出帧。PCM 就绪后 3 秒未出现 native 输出确认时停止并报告失败；只打开驱动不显示播放成功。共享模式保持原来的普通启动路径。
- 信号路径显示 native 返回的实际硬件采样率、声道、设备、格式和缓冲。空闲配置尚未打开设备时明确显示未确认，不能把请求值当实际值。
- ASIO DoP/Native DSD 的按速率能力探测已接入。探测前必须停止播放并释放自有驱动进程，避免与自身的 ASIO 句柄竞争。
- `AudioOutputConfiguration` 接入 PCM、DoP/SDM 和 Native DSD/SDM 的配置形状；DSD 保持双声道并遵循原有 native Pro DSP gate。UI 当前根据宿主授权启用 SDM 格式选择。这里的 SDM 输出不等同于 DSF/DFF bit-perfect 直通。

## 短测事实

本机编译能力包含 shared/exclusive/asio/ks；原生枚举得到共享 8、独占 8、ASIO 7、KS 13 个设备/驱动。ASIO 列表包括 Realtek ASIO、TEAC ASIO USB DRIVER 和 Voicemeeter。

八声道 deferred bootstrap、重建回双声道、无效 ASIO 驱动拒绝后恢复真实共享播放的短测通过（约 1.37 秒）。此前普通播放 smoke 仍通过。

最后一次核心短测在并行队列模块拆分完成后再次通过（约 1.43 秒）。专业输出设置首次接入时 Desktop 构建通过；当前整桌面的最后 Headless 复验被并行新增歌词控件的 `GetTextBounds` 参数类型及 geometry `PushClip` 编译错误阻塞，尚未取得本次 ASIO 面板最终截图。该限制与已通过的独立输出模块短测分别记录。

Realtek ASIO 尝试返回实际 ready：48 kHz、2 声道、int24lsb、528 帧缓冲（请求 512 帧）。解码已产生 firstPCM，但 native sourceFrames 保持 0，未确认实际播放；因此不记为 ASIO 硬件验收通过。由此补上输出帧确认及阻塞驱动隔离恢复。

TEAC、Voicemeeter、真实 6/8 声道、KS、独占 DAC、DoP/Native DSD 和听感未完成硬件验收。没有完整重编 C++、打包或发布；继承的 SDK/license 文件保留，发行检查单独执行。

运行 `tests/Echo.Output.Smoke` 可复现模式枚举、声道初始化、失败回退；`--skip-hardware` 跳过已知受设备状态影响的短硬件尝试。整轮验证保持原有 120 秒预算。
