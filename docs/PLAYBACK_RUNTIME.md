# 应用播放核心与本机 Zone

日期：2026-10-02。

后续统一状态与分类订阅见 [ZONE_STATE_SUBSCRIPTIONS.md](ZONE_STATE_SUBSCRIPTIONS.md)，专辑身份与来源见 [ALBUM_METADATA_IDENTITY.md](ALBUM_METADATA_IDENTITY.md)。

播放压力下的后台准入与诊断见 [PLAYBACK_WORK_ADMISSION.md](PLAYBACK_WORK_ADMISSION.md)。

## 当前职责

`Echo.Application/Playback/PlaybackRuntime` 不引用 Avalonia。它是本机 Zone、播放状态存储和系统媒体控制的唯一生命周期 owner。桌面应用和独立 `Echo.Player` 都调用这个核心；各进程拥有独立实例，目前没有跨进程或手机遥控协议。

`PlaybackZone` 的稳定 ID 为 `local`，名称为“本机”。其 `PlaybackSessionCoordinator` 继续拥有队列、历史、循环/随机、播放命令顺序和原生确认状态。`PlaybackOutputState` 单独表达设备名称、后端、请求配置、确认配置及渲染状态。更换输出设备沿用原会话、队列和 Zone ID，并复用已有输出回退逻辑。

```text
App / Echo.Player
  └─ PlaybackRuntime（退出时保存并释放）
       ├─ 本机 Zone → PlaybackSessionCoordinator → Native Audio Host
       ├─ IPlaybackCheckpointStore → PlaybackStateDatabase
       └─ PlaybackMediaTransport → 平台系统媒体控制
                    ↑
         PlayerViewModel、队列、歌词、输出设置
```

## 控制端与退出

控制端通过注入 Zone 接入已有会话，先订阅再读取当前播放快照。UI 合并最新版本，断开时取消订阅、清空待交付快照并释放自己的队列展示。断开和重新连接控制端不会关闭音频宿主，也不会重建真实队列。

正常桌面由 `App` 持有播放核心；窗口清理自己的视图资源，最后窗口关闭后应用保存并释放核心，再退出。没有新增默认后台常驻或托盘策略。预览入口不传入核心时，窗口为其独立核心负责释放。复用已有核心的窗口须传入 `playback`，不创建第二个播放 owner。

系统媒体控制由核心持有，窗口仅订阅诊断并提供重试入口。退出先解除系统命令入口，再保存会话、释放 Native Host 与状态数据库。共享启动和退出任务只建立一次；控制端取消等待不会取消其他控制端的初始化。

## 存储与资源

状态库仍是新应用独立的 `ECHO Avalonia/playback.sqlite`，schema 不变，不打开旧 ECHO 曲库。`PlaybackStateDatabase` 的 writer lock 与 SQLite lane 继续保证单 owner。

保留队列修改后的自动保存：待保存信号最多一个、合并积压，在后台导出有界 checkpoint 并串行写入。退出再保存最终状态。读盘启动在后台执行；恢复保持暂停，且迟到读取不能覆盖新的用户操作。保存/恢复诊断不能让控制端异常中断音频或资源释放。

沿用队列窗口 1000 项、历史 500 项、checkpoint 512 KiB、DSP 128 项，以及已有命令/RPC 上限。Native Audio Host 保持实际时钟、解码、DSP 和输出 owner，界面不会把已保存的设备请求当作当前已确认输出。

## 范围与回滚

目前提供一个真实本机 Zone。多区域并行、队列跨 Zone 转移、网络输出、手机连接和多房间同步仍未实现。曲库查询与扫描的所有权不在本次改动范围内。

回滚恢复原桌面播放装配及持久化生命周期即可；原 checkpoint 格式仍可读取。不要同时启用原窗口持久化与核心持久化，两者会争抢同一状态库 writer lock。

## 短验证

`dotnet run --project tests/Echo.Playback.Smoke -- --runtime-only`：共享初始化、单次退出、真实独立 SQLite checkpoint、暂停恢复、迟到读取、等待取消、设备切换保持 Zone/队列、系统命令路由及保存失败后的资源释放。

`dotnet run --project tests/Echo.Playback.UiSmoke`：实际 Avalonia 播放栏绑定、两个控制端、断开/重连、真实静音原生宿主及窗口独立生命周期。Headless 绘制不能代替完整桌面/跨平台验收。

本轮结果：上述两组定向检查通过，注入同一核心的 `DesktopComposition` 窗口关闭后，宿主与队列继续存在；退出核心后设备确认事实撤销。独立 `Echo.Player` 构建为 0 警告、0 错误。实际 Headless 预览为 `misc/playback-ui-smoke/zone-output.png`，日志在 `misc/zone-validation/`。

构建、检查和重跑累计约 90 秒，包括首轮 UI 检查达到 60 秒上限后停止自有进程。修正检查程序在异常退出时依赖 UI 上下文等待释放的问题后，定向 UI 检查通过。混合播放检查曾在并行队列改动的 `remove after play` 断言失败；本轮只声称上述核心/Zone 定向检查通过，没有复跑或宣称全部播放检查通过。其间并行专辑页面编译错误随后已消失，最后桌面定向检查成功构建并运行。
