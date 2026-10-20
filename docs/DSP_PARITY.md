# DSP 对齐状态

## 其余模块专用界面（2026-10-20）

移除 `DspGenericView`。`DspModuleHost` 为当前模块按需创建专用编辑器；离开时撤销绘图订阅和按住试听。模块页复用小型参数、选择器、开关与运行读数组件，不再把不同 DSP 能力堆在同一份通用表单里。原生请求仍由原有 Playback lane 串行协调，资源仍经 Core 保存；没有新增生产数据库、独立音频连接或 DSP 计算。

| ECHO 参考工作台 | Avalonia 专用页面与组织 |
| --- | --- |
| DspListeningChrome / DspListeningWorkbench | DspListeningView：当前声音、方案列表、保存/套用/删除、独立机架草稿区；机架待应用时禁止保存/套用方案 |
| DspRackPanel / DspRackRow | DspRackWorkbench / DspRackView：处理链编号、模块与实例、固定阶段、重排、添加/复制/删除、选中实例编辑 |
| ReplayGainControlSurface | DspReplayGainView：响度与增益来源、前级、防削波、曲目/专辑与实际应用读数 |
| HeadroomControlSurface | DspHeadroomView：增益读数、预设、按住对比 0 dB、输出峰值与限幅确认 |
| EchoSrcControlSurface | DspSrcView：输入/请求链路、倍率、计算后端、抖动/位深、1x/Nx 滤波器与特性图、宿主运行指标 |
| EchoSdmControlSurface | DspSdmView：DSD 输出/传输率、质量/环路、听音组合、计算/FIR/前调制、插槽预览与实时预算 |
| HeadphoneCorrectionPanel | DspHeadphoneView：分页 OPRA 资源、作者与来源、校正曲线、原始/听感模式、前级、参数及本机预设/导入导出 |
| RoomCorrectionControlSurface | DspRoomView：脉冲资源、校正增益/快捷值、卷积路由、声道/采样率/taps/延迟确认 |
| ChannelControlSurface | DspChannelView：左右输出示意、平衡/增益、延时、分频补偿、单声道/反相/交换 |
| SafetyControlSurface | DspSafetyView：输出保护、限幅开关、实际衰减与信号检查 |
| CompressorPanel | DspCompressorView：传输曲线、检测、阈值/比率/补偿/拐点、快慢/混合与高级侧链 |
| MultibandPanel | DspMultibandView：三段选择及独立启停、交叉分频、整机混合与分段动态/响应时间 |
| DeEsserPanel | DspDeesserView：检测频率/Q、按住监听、衰减、快慢/混合与参数曲线 |
| LimiterPanel | DspLimiterView：保护状态、衰减/峰值、自动或手动上限及释放；自动模式禁用手动上限编辑 |
| CrossfeedWorkbench | DspCrossfeedView：Mid/Side 响应、拖动截止、经典/BS2B、对应参数及原版预设 |
| VirtualStageWorkbench | DspVirtualStageView：虚拟声源/角度/听音位置、强度与反射、原版三组预设 |
| StereoFieldWorkbench | DspStereoFieldView：M/S 参数图、拖动宽度、中置/侧向增益与原版宽度预设 |
| ChannelMatrixWorkbench | DspMatrixView：输入/输出路由、正负系数、四组系数与直通/交换/单声道/Mid-Side |

### 当前契约的显示边界

曲线和声场图是参数模型，滤波器图沿用原版的特性示意；均不作为音频实测。输出峰值与限幅/SRC/SDM 的运行值只读现有宿主快照。当前连接没有提供动态模块实时衰减轨迹、目标 LUFS/批量响度分析、整链旁路与自动响度匹配的业务契约；相关界面保持未提供/空读数，不用估计或其他模块的数据冒充。没有音源时不把默认 48 kHz 显示为实际输入率。

听音页的嵌入机架只保存当前页面的有限草稿，与父页共用同一播放客户端和资源客户端；不创建服务 owner。离页丢弃机架草稿。按住 Headroom/齿音试听在松开、失焦、切页和释放时恢复，迟到回复不重新启用试听。

验证入口：`tools/Echo.DspPreview`。使用独立测试资源目录与独立原生宿主，覆盖模块专用控件绑定、键盘参数编辑与原生回读、多段嵌套布尔参数、试听释放、Core 方案保存/删除与嵌入机架提交，以及各模块明暗与窄窗口截图。当前默认构建输出被并行 .NET 进程占用，使用 `obj/DspAll/`、`bin/DspAll/` 独立输出；未停止或替换用户运行中的应用。截图在忽略目录 `artifacts/dsp-all-modules-preview/`。真实音频设备、跨机器与其他桌面平台不包含在界面验收内。

整套专用页面曾随 Desktop 构建通过。最后整桌面复核遇到并行文件的暂态错误：歌曲行字符串错误随后已变更，之后信号路径面板缺少 `DiagnosticsExpander` / `DoctorExpander`。没有修改这些文件。最终将当前全部 DSP C#/XAML 与相同交互检查复制到忽略目录 `misc/dsp-final-check/`，仅替换验证命名空间以避免与最后一次已构建壳程序集中的类型重名；当前 DSP 源码隔离构建 0 警告/0 错误，使用原生产控件逻辑及真实独立宿主复核。这个结果不等于最新整个工作区已完成构建。

## 界面同步（2026-10-20）

本轮界面对照只读的 Electron ECHO 检出 `542e7c66c`：`DspSidebar.tsx`、`dsp-sidebar.css`、`dsp.css` 最终覆盖规则与 `eq-dsp-workbench.css`。保留现有 19 个模块及 Core／Playback／独立原生宿主的控制边界，修改集中在 Desktop 展示层。

- 模块侧栏沿用品牌、分类、状态摘要和底部输出；高级处理入口在导航滚动区中展开。选中用局部底色和图标反馈。窄于 860 的工作区切换为顶部横向导航，调整窗口时保持当前模块可见。
- EQ 使用独立工具栏、预设、简洁／专业视图、启停和刷新；曲线填充剩余高度，专业模式的数值台、频段条和前级操作接在曲线下方。视图切换不改变频段、选中项或草稿；A/B、撤销、还原和应用沿用现有实现。
- 页面进入及显式刷新通过已有播放会话串行读取模块摘要，回复随读随释放，不常驻全模块 JSON，不新增轮询、音频连接或实时分析。缺少运行数据继续显示未确认，不把配置开启当作实际音频处理或 bit-perfect 证明。
- `Echo.DspPreview` 使用生产 Avalonia 控件与独立测试宿主检查 19 个入口、明暗主题、紧凑窗口、模式切换、折叠导航、键盘编辑和齿音监听。截图输出在忽略目录 `artifacts/dsp-migration-preview/`。真实设备播放、听感及 macOS／Linux 运行不属于本轮界面验证。

参考 echosteam 已提交版本 `a44881fcc21a5d374d3bbd42aba1263b7ec71a28`。参考仓库 `/Users/moekotori/echo` 只读，未修改；原生数据面来源与固定版本校验见 [AUDIO_ENGINE_PARITY.md](AUDIO_ENGINE_PARITY.md)。

## 页面和宿主契约

19 个入口均有生产 Avalonia 页面，沿用参考的模块组织，统一深色绘图区、参数排版、主题资源与固定操作栏。参考 Roon 的信息层级，不逐像素复制其界面。参数仍通过现有 Playback owner 的串行命令送入独立 Audio.Host，Control 不建立第二条音频连接。

| 页面 | 可操作内容 |
| --- | --- |
| 听音方案 | Core 保存、选择、套用、删除；恢复全部原生模块、机架资源及已保存的输出处理配置，设备身份使用当前输出 |
| DSP Rack | 11 个基础模块完整 sequence，新增/复制/删除/混排最多 8 个实例；EQ、耳机校正、交叉馈送、运算处理及最多 2 个卷积实例 |
| 音量均衡 / Headroom / 输出安全 | 曲目/专辑增益、防削波、前级、处理链增益与原生安全限幅 |
| 升频 | 关闭、2/4/8/16 倍、48 kHz 兼容；41 个 1×/Nx FIR 配置、CPU/CUDA 选择、抖动与位深 |
| DSD 转换 | PCM/DoP/Native DSD、采样率家族、8 种调制质量、决策环路、19 种听音预设、自定义 FIR、三档 FIR 强度与前调制器 |
| 参数 EQ | 31 段、6 种滤波器、作用域、最多 4 个活动动态峰值频段、前级、强度、倾斜、自动前级、预设导入/导出/保存/删除 |
| 耳机校正 | 独立原生链、OPRA 搜索/分页/更新、作者来源与授权、原始/听感参数切换、完整精度、REW/APO/JSON/CSV 导入及 JSON 导出 |
| 房间校正 | Core 卷积资源选择、导入、删除、清除、trim 和启用；以原生回执显示资源及待播放状态 |
| 声道工具 / 声道矩阵 | 平衡、左右/分频增益、延时、反相、单声道、交换；矩阵直通/交换/单声道/Mid-Side 快捷方案 |
| 压缩器 / 多段压缩 / 齿音抑制 | 全参数；多段低/中/高频分组编辑、静态传输曲线；齿音按住监听，松开、失焦、切页与释放时停止 |
| 真峰值限制器 | 自动 ceiling 的明确 null、手动 −6…−0.1 dBTP、release 10…400 ms；原生处理状态与实际衰减回报 |
| 交叉馈送 / 虚拟声场 / 立体声场 | 参考对应模块的模型、增益、频率、方位、反射、声场宽度和中侧参数 |

机架及 SRC/SDM 只在应用后提交，切页和关闭不提交草稿。其余模块保留有界、低频试听；应用后才加入撤销。A/B 是当前模块的两份页面快照，不建立全模块历史。运算处理支持 EQ、gain、delay、invert、mute、mix、IIR；最多 16 步，每步最多 31 段/64 路由，宿主整图验证延时预算及 IIR 稳定性。

输出选择、动态曲线与配置启用不等于已经实际处理音频。SRC/SDM 面板展示宿主的实际 active/backend/耗时/回退数据，缺数据时明确显示未确认；无设备、无授权、采样率家族或模式不匹配均保留草稿并显示错误。静态 EQ/压缩曲线不冒充实时测量。

## Core 资源所有者与预算

新增 `Modules/Echo.Dsp` 由现有独立 Core.Host 组合，与资料能力共享同一个 Core 进程及 owner 锁。Control 使用 `IDspResourceClient` 与带版本、请求 ID、Core 身份、实例代次、revision 和错误的当前用户 IPC。服务端同时最多 4 个连接，客户端最多 8 个 pending，消息最多 512 KiB；慢操作和进程退出都有取消/超时。普通客户端释放不关闭 Core。

- 预设最多 32 个、听音方案最多 16 个，资源库 512 KiB，原子写入；旧 `dsp-library.json` 只由 Core 一次迁移。桌面不再直接读写生产 DSP 资源库。
- OPRA 仅在搜索时下载，流式解析并落 SQLite 索引；每页 30 项，页面只保留当前参数对。限制下载/单条/总记录与查询长度；更新是显式动作，无后台轮询或整库 ViewModel。
- OPRA 作者频率和增益保留，架式 Q 转为原生 S；不支持的作者滤波器或范围整笔拒绝。来源、作者、链接、CC-BY-SA-4.0 与修改说明随保存及导出保留。听感参数与作者原始参数分别建立。
- REW/APO/JSON 参数导入保留作者参数；CSV 采样曲线明确为 31 段近似，增益限制 ±12 dB。个人 EQ 与独立耳机链的范围、作用域和动态能力分别检查。
- 卷积最多 32 包，每文件 8 MiB、每包 32 MiB、整个资源库 256 MiB；每包最多 64 个过滤器、131072 taps、262144 系数，沿用原生预算。WAV/FLAC/AIFF 与多采样率 ZIP 由 Core 保存至私有目录，以 bank ID 解析原生 manifest。删除是用户显式动作；引用已删除资源的方案会报错。
- ZIP 含 CFG 时配置是权威布局；解析 Convolver v3 的权重、输入/输出延时、系数声道与路由，编译为 Float64 WAV，不把引用文件误当成独立候选。CFG 当前支持 WAV/PCM Float32/DBL Float64 系数；压缩系数明确拒绝并提示先导出 WAV。未实现 ALAC/M4A 资源导入，不宣称所有资源格式完全对齐。
- 机架只为选中节点建立编辑器，卷积私有 nativePath 不暴露为用户文本输入。恢复方案时重新解析 bank ID。尚未播放时，按宿主确认的“资源已载入，等待格式”状态处理，不伪装为卷积已经进入数据面。

41 个 FIR 配置和 19 个听音配对以小型嵌入资源维护。整数倍链拆为宿主支持的 2× 阶段；系数和最小相位转换在控制面的后台任务中准备，不在 UI 线程、实时回调或逐帧 IPC 中计算，也不常驻一套大系数缓存。

Desktop 构建按实际 Core 输出目录复制入口、程序集和 `runtimes` 原生依赖，避免只有 C# 编译成功而 SQLite 无法加载。

这是 DSP 资源能力迁入 Core，不代表整个产品的 Core/Control/Bridge 分离已完成。

## 必要验证

- Desktop/Core/新增模块与预览工具在独立 `obj/DspParity`、`bin/DspParity` 输出中聚焦构建通过。保留已有 Avalonia 无公开构造器资源警告。
- 实际 Avalonia 渲染全部 19 个入口，检查导航、高级折叠、键盘数值输入、light/dark、920×780 和 1140×700 布局、固定操作栏及坐标标签边界。截图在 `misc/dsp-all/screens/`，过程见 `tools/Echo.DspPreview/design-qa.md`。
- 真实新宿主参数回读覆盖 EQ/独立耳机链、完整机架草稿边界、实例/运算、A/B、动态 EQ 超限整笔拒绝、限制器、多段、齿音、声场与声道等。增加 Core 预设保存/导入、房间/机架卷积资源、听音方案保存/恢复/删除及齿音监听释放检查；失败的 IR 加载不能覆盖之前有效的恢复记录。卷积回执检查包括无播放格式时的待处理资源状态，不等同于设备正在运行卷积。
- 隔离资源目录与独立 named pipe 检查真实 Core DSP endpoint：保存后查询、REW 严格导入及错误回执、双采样率 ZIP、CFG 路由、FIR 分级及 DC 增益。使用真实 OPRA 数据库完成一次下载、品牌查询及原始/听感参数转换；后续复查复用该证据缓存。
- FIR 两组系数直接与只读 echosteam TypeScript 实现比较：线性 `echo-hb` 255 taps、最小相位 `echo-gauss-hires-mp` 1023 taps，最大绝对差分别约 `8.35e-9`、`5.33e-9`；不据此推断全部配置的性能或听感。
- 不运行全量测试、长时间播放、整库扫描或性能大矩阵。真实设备听感、DoP/Native DSD/CUDA、Windows/Linux 设备、AOT 发布及整棵进程树占用对照仍需对应环境验证。

## 页面保真修正（2026-10-20）

这一轮仅修改 Control 展示与输入映射，沿用现有 Core 资源客户端、播放会话和原生回执。没有新增数据库访问、音频连接、后台定时器或 DSP 实现。

- SRC / SDM 对照 `EchoSrcControlSurface`、`EchoSdmControlSurface` 与对应 flat CSS，恢复大读数、四段计划、运行侧栏、横向控制台及主图 / 详情比例。SRC 实际输出率独立于请求倍率；SDM 使用噪声整形图区域，不再拿 FIR 曲线替代 NTF。
- Headroom 对照 `HeadroomControlSurface`，恢复大增益读数、线性幅度、带刻度推子、预设条、余量建议侧栏和电平预算。
- 四个空间页对照 `spatial-editor-workbench.css` 使用 1.35:1 主图 / 控制台，图表随主题变化并增加参数摘要。小于 760 像素时纵向排列；不再使用等宽、等高的通用表单布局。
- 多段压缩 / 去齿音恢复状态与衰减读数、可交互分频图 / 检测带、三个控制分组和衰减轨迹区域。分频图点选频段、检测带拖动与数值编辑共用同一草稿和应用路径。
- 房间校正恢复宽资源列与窄余量侧栏，未载入 IR 时收起增益及余量侧栏；声道工具恢复轻量的左右摘要条；输出安全调整状态与操作区比例。
- 强度、声场宽度、干湿比和链接比例按 ECHO 显示百分比。只转换编辑数值，底层契约仍使用原有 0..1 / 0..2 参数，避免把 100 写成音频增益。

当前连接没有目标 LUFS / 曲库响度分析、动态模块衰减历史和 SDM 调制器系数契约：对应设置禁用并说明原因；图表保留空读数，不绘制虚构测量或用其他模块的数据代替。源页面的完整行为仍受这些接口边界限制，不宣称逐像素及全部功能等同。

截图与检查记录：`artifacts/dsp-fidelity-preview/`、`artifacts/dsp-fidelity-ui.log`。通过独立 Avalonia 渲染与真实原生宿主回读检查；不代表真实音频设备、跨机器或 AOT 发布验收。

本轮最终 DSP 源码独立编译 0 警告、0 错误；19 页的渲染、主题、窄窗口及原生参数回读通过。新增去齿音检测带拖动、多段分频图点选检查，并修复透明绘图区空白处不能命中鼠标的问题。OPRA fixture 数据库在本轮不存在，该项未重跑。最新整桌面构建被并行艺术家页 `ArtistDetailView.axaml:46` 的 `IsOverview` 绑定错误阻塞；未修改艺术家页。
