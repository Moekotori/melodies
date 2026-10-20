# 专辑无缝与随机避重

2026-10-20。入口：设置 → 播放 → 播放偏好 → 队列衔接与播放顺序。

## Owner 与行为

- Core 的 `PlaybackSessionCoordinator` 持有策略、播放顺序和近期歌曲 ID；Control 通过类型化 RPC 提交选择。设置保存到 Core 配置，歌曲 ID 随播放 checkpoint 保存，不依赖窗口存活。
- 随机避重支持不避重、最近 25 首、最近 100 首。按 TrackId 识别不同 occurrence，优先播放近期未听的候选。候选全部近期听过时仍保留队列，正在播放歌曲的其他 occurrence 排在其他候选后面；明确插入的“下一首”保留最高优先级。
- 播放中新增、移除及更换队列不会清除近期 ID；上一首导航记录仍独立维护。近期记录最多 100 个 ID、32768 个 UTF-16 字符，不保留额外曲目 DTO 或加载整库。旧 checkpoint 可按已有导航历史恢复 ID。
- 原生宿主继续负责解码、有界 PCM FIFO、采样边界、播放时钟及实际换曲回执。Core 只采用通过 operationId / queueRevision 校验的宿主状态。
- 队列调整和 seek 后重新准备无缝链。已经开始输出的 continuation FIFO 也包含当前歌曲；若后续顺序改变、关闭无缝或改为不兼容模式，按最近宿主确认位置重新打开当前歌曲，保留暂停状态，避免继续输出旧顺序。此类主动编辑可能短暂重开输出，不承诺编辑动作本身无缝。

## 当前边界

无缝用于顺序／列表循环的普通本地 PCM 文件；随机、AutoMix、A–B、非 1 倍速、DSD／SDM、HTTP／网盘及 CUE 范围音源不进入此路径。独占、ASIO、KS 或 ECHO SRC 还要求已知且相同的源采样率。不能准备时继续普通播放。

沿用原生宿主与 ReplayGain 时间线的有界设计：一次最多准备 31 个后继，按需解码到固定 FIFO，而非整张专辑 PCM 常驻。超过此窗口的后继仍由正常 EOF 路径接续并重新准备；不承诺第 32 个后继边界无缝。整库分页补足、无限连续预解码与跨机器设备验收不在此变更中。

## 聚焦验证

- `dotnet run --project tests/Echo.Playback.Smoke -c Release`：原有队列测试，以及 0／25／100 首、同曲不同 occurrence、小队列、手动插队、失败回滚、换队列、checkpoint、seek、A–B 清除、预取取消和已开始的链调整。
- `dotnet run --project tests/Echo.Playback.Smoke -c Release -- --chain-audit`：原生启动取消、专业输出错误、事件通道存活、迟到 AutoMix 与网盘 EOF 回归。
- `dotnet run --project tests/Echo.Playback.Smoke -c Release -- native/build/audio-host/Release/echo-audio-host.exe --gapless-native`：Windows 真实宿主静音 WAV 输出，定位后三次 `gaplessAdvance` 到完整 drain；第二组在首次换曲后暂停、重排，再确认原生按新顺序无缝接续。无声夹具不等于听感验收。
- `dotnet run --project tests/Echo.Core.IpcSmoke -c Release -- E:/melodies`：两个控制连接共享避重计划、Core 配置保存与进程重启后的行为恢复；音频端使用协议模拟进程。
- Core.Host／Bridge.Host Release 构建通过。桌面最后一次构建被并行 DSP 修改中的 `EqCurveControl.cs` → `EqResponse.Db` 引用错误阻塞；未进行这次设置帮助文字的运行时界面验收。

1000 项、Release、同进程预热 100 次后测量 2000 次纯策略调用：原固定 8 首建立顺序为 225.6 μs／33208 B，新配置 100 首为 207.0 μs／42496 B；新增的稳定后继检查为 155.7 μs／31104 B，顺序未变化时复用原数组。这里只比较策略计算与线程分配，不推断整体进程内存、后台 CPU 或真实设备延迟改善；复现夹具保存在本机 `misc/playback-continuity-budget`。
