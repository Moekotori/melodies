# 睡眠定时与 A–B 片段循环

2026-10-02。特色功能与可设置功能的首批增量，旧 ECHO 只读。

## 用户入口与设置

- 主播放栏“更多”中的“定时与片段循环”；歌词底栏“更多”共用同一个面板和 owner。
- 定时时长为 1–720 分钟，支持到期暂停、停止、停止后退出；可以重新定时或取消。
- “设置 → 播放 → 睡眠定时与片段循环”可选默认时长、默认动作、开始/取消定时和清除循环。面板自定义时长也能显示在设置中。
- 默认参数通过现有 `FeaturePreferences` 与独立 `preferences.json` 保存。恢复默认参数不会重建过期的定时任务。
- A/B 可用当前原生位置标记，或按秒直接输入；片段至少 0.5 秒，B 点距结束至少 0.25 秒。启动时定位 A，收到有效原生进度到达 B 后重新定位 A。
- 暂停保持片段；切歌、重开同一项、停止、故障和拖出片段清除循环。直播不可设置循环。未确认的定位、队列或定时动作显示失败，不自动重试。

## 实现与资源

| 模块 | 职责与所有权 |
| --- | --- |
| `Echo.Application/Playback/SleepTimerService.cs` | 一个 deadline、一个 1Hz worker 和一个零等待准入 gate；不依赖窗口可见性，到期走原播放命令 lane |
| `Echo.Application/Playback/PlaybackRuntime.cs` | 创建、持有并在播放会话释放前关闭定时服务 |
| `Echo.Playback/PlaybackSegmentLoop.cs` | 一个临时片段状态；同一命令 lane 验证、定位和恢复原生队列 |
| `Echo.Playback/PlaybackNativeEvents.cs` | 使用已校验 operation/epoch 的原生进度；定位后旧进度不能重复触发 |
| `Echo.Desktop/Features/Player/PlayerToolsViewModel.cs` | 局部参数和最新状态；至多一个待投递 UI 刷新，不订阅每帧 UI 时钟 |
| `Echo.Desktop/Features/Settings/Runtime/PlaybackToolsSettingsModule.cs` | 实际设置消费者、输入验证和动作提交；沿用原设置保存 owner |

循环启用时原生队列暂投影为当前项、关闭自动推进；UI/持久化队列顺序不变。取消恢复原有模式，手动下一首仍可使用。现有 C++ 宿主源码说明 `queue.set` 更新 revision 并取消待推进；本轮没有修改原生宿主。

窗口退出取消面板待提交命令、解除订阅和退出回调；Runtime 取消并等待定时 worker。没有新数据库、HTTP、扫描任务或凭据存储。后台定时不随页面离开而取消，这是用户启动倒计时后的预期行为。

## 验证与边界

核心项目当前源码编译通过。完整工程构建遭遇并行扫描 `FolderSourceSet` 参数不匹配；Desktop 定向构建另遇并行字体/主题缺失及重复声明。未修改这些模块或停止用户运行中的应用。

`tools/Echo.PlaybackToolsPreview` 在隔离模式下直接编译本批生产 ViewModel、XAML、设置模块及主题资源映射，复用已有 Desktop 壳产物和本轮核心程序集。20 项检查通过，覆盖真实 Avalonia 按钮输入、暂停/停止/退出、失败、取消、隐藏继续计时、配置恢复、循环回跳、旧事件、暂停保护、队列恢复、同项重开、切歌与直播限制。原生音频响应为夹具，不能作为物理设备听感证明。

浅色、深色、英文和窄窗口原生控件截图：`misc/playback-tools/previews/`。日志：`misc/playback-tools/checks/`。构建、失败修复及全部重跑累计约 64 秒；单项未超过 60 秒。

尚未迁移：定时淡出、托盘倒计时菜单、片段书签及旧书签导入；未验收真实设备、运行中窗口、三个平台和长期行为。A–B 是原生进度事件驱动的定位循环，不宣称采样级无缝循环。

正常定向入口为 `dotnet run --project tools/Echo.PlaybackToolsPreview`。隔离预览要求先有本轮核心快照 `misc/playback-tools/core/`，使用 `PlaybackToolsIsolated=true`；它不代替整应用构建。

## 回滚

移除 Bootstrap 的工具装配、两处面板入口及四项设置目录即可撤销 UI；原生/旧 ECHO 和生产库不变。关闭新 Runtime 后定时 worker 结束；片段状态不跨启动保存。没有删除旧版实现。
