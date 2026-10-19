# C# 新宿主适配

2026-10-19。基于 [音频引擎对齐基线](AUDIO_ENGINE_PARITY.md)，所有修改仅在 echocsharp。

## 主播放协议与生命周期

- 校验 ready 的协议、后端版本、运行时设备配置能力和宿主角色；缺少新版构建身份的旧二进制会明确报错，不再默认为已适配。校验失败终止本客户端启动的宿主。
- `InspectNativeHostAsync` 在播放 owner 的命令 lane 中读取编译能力，对比磁盘文件与运行进程的 buildId。查询结果分别表达 CUDA、SDM 健康监控、盲听预渲染、硬件音量工具和 E-AC-3 HDMI 工具的编译能力；不等于当前设备已经支持或打开。
- 队列带 `cursorOperationId`。手动打开后，所有输出模式都将歌曲 occurrence 绑定到宿主确认的 operation，防止同一文件重复入队时沿用旧游标；宿主已自动推进时，旧 operation 无法覆盖其游标。
- seek 采用宿主返回的位置，并为新 operation 重新建立输出确认，避免加载中 seek 后继续等待旧 operation 的 started 事件。
- 重建与故障时清除旧宿主身份和能力，保留既有 epoch、请求数量、消息大小和退出边界。

## 输出设置与遥测

- 支持 `bufferSize=0` 的驱动管理缓冲；音频设置重新应用时保留同格式下的 SRC、SDM、抖动等完整 processing，以及共享后端和延迟策略。
- Linux 通过真实独占设备枚举显示 ALSA 独占，并允许请求其 Native DSD。设备是否真正支持仍由宿主确认。本次在 Mac 上开发，没有 Linux 设备验收。
- SDM 编辑器保留 Native DSD 格式及已有调制/升频配置，区分传输率与 Native DSD 位速率，提供宿主接受的质量档位。SRC 编辑器接入 2/4/8/16 倍配置，移除 FIR 系数的小数截断；原生约束、权限、准入和实际设备结果继续生效。
- 性能快照解析宿主进程内存及 SDM 实时窗口：健康状态、流状态、是否独立校准、处理 P99、DSD 位队列、渲染欠载。DSD 位数不冒充 PCM 帧或缓冲毫秒，未知 private bytes 保持为空。

## DSP 与已有设置

- 机架完整保留 11 个基本模块；排序保留附加滤波器、sequence 插槽及 procedural 配置，听音方案也保存完整机架。
- 识别旧 C# 8 模块顺序，按新宿主默认位置插入新增模块，保留旧模块相对顺序。此适配用于直接套用旧方案及重建后的恢复，不自动修补任意错误顺序。
- 耳机校正使用 `headphoneCorrection.*`，与个人 EQ 分离；原生校正值不再经过界面小数截断。频率、增益、Q 和曲线显示范围按校正域处理。
- 接入多段压缩、De-esser 和虚拟声场编辑；多段参数写入真实 bands 层级。BS2B 模型、截止频率与交叉衰减使用原生契约。
- 所有操作通过既有播放 owner 提交，Control 保留草稿和展示状态，不创建第二套播放器或音频连接。

## 聚焦验证

```sh
dotnet run --project tests/Echo.Playback.Smoke -- --adapter-contracts
dotnet run --project tools/Echo.NativeAdapterSmoke -- <当前平台宿主路径>
dotnet run --project tests/Echo.Playback.Smoke -- <当前平台宿主路径> --native-only
```

契约检查覆盖高级配置保留、缓冲 0、DSD 单位、遥测和旧宿主拒绝。独立适配 smoke 使用实际 Desktop ViewModel 与原生宿主，覆盖独立耳机校正参数、完整机架/附加滤波器及新增模块参数往返。播放 smoke 覆盖静音输出、暂停/seek、EOF/FIFO、同文件重复 occurrence 游标、崩溃恢复、旧机架迁移和释放。

独立 smoke 避开旧 `Echo.Preview` 中与此次修改无关的 `SongDropSmoke` Avalonia 接口编译错误；不修改或删除那个检查。Desktop 构建仍保留已有 Avalonia 警告。

本轮已通过契约检查、实际 DSP ViewModel/宿主参数往返，以及含旧机架迁移、驱动管理缓冲和进程恢复的播放短测。随后新增的同文件手动游标用例及最终桌面重编，在共享工作区并行编译期间达到时限；停止本轮自有验证进程，没有中断其他会话。该最后一轮不记为通过，也不把较早通过的检查描述为最新全部源码完整验收。

本轮不做 Windows/Linux、ASIO/KS/DSD/HDMI/CUDA 硬件验收，不做 AOT 发布、打包或长期播放测试。Windows 的旧运行库需要用更新后的构建脚本重编；正式发行依赖与签名仍按平台验收。编译能力查询不等于把独立 HDMI/盲听工具接成默认播放后端。
