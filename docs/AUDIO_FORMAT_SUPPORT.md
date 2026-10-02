# 音频文件格式对齐

2026-10-02。清单直接对照旧 ECHO `src/shared/constants/audioExtensions.ts`，共 53 项；统一于 `Echo.Contracts/Media/AudioFormatCatalog.cs`。文件选择、播放队列、扫描候选和本地源验证使用同一清单。

## 当前路径

普通音频及含音轨容器由现有 C++ Native Audio Host 的 libav 流式解码处理。扫描保持 WAV/FLAC 等快速头部解析；扩展格式和缺少技术属性的结果走独立 C++ FFmpeg 元数据探测。探测限本地文件、3 秒、累计 16 MiB IO、1 MiB probe、64 个 probe packet 和 32 个 stream；不运行整首 PCM 解码循环，不提取封面、不做音频哈希。原有嵌入标签与来源保留。

原始 TrueHD/DTS 可能没有时间戳与时长；元数据保留未知时长，不把可解码音频判为不支持。MP4/MOV/MKV/WebM 必须有可读音轨；无音轨视频会被拒绝。后缀出现在列表不等于受保护文件、损坏文件或任意编解码器已获支持。

## 格式范围

| 类型 | 对齐范围 |
| --- | --- |
| PCM / 波形 | WAV、WAVE、BWF、W64、AIFF、AIF、AIFC、AU、SND、VOC、CAF；RF64 经 native 元数据回退 |
| 无损 | FLAC/FLA、ALAC、APE、WavPack、TTA、TAK |
| 压缩 | MP3/MP2/MP1、AAC、Ogg/OGA/Vorbis、Opus、WMA、Musepack、Speex、AMR |
| 多声道编码 | AC3、EAC3/EC3/DD/DDP、TrueHD/THD、MLP、DTS；实际保留声道依输出策略 |
| 音频容器 | M4A/M4B/M4P、MP4、MOV、MKA、MKV、WebM，按实际音轨探测 |
| DSD 源文件 | DSF、DFF 由 libav 解码；DSD 源文件能播与 bit-perfect/DoP/Native DSD 输出是不同能力 |
| 定义文件 | CUE 复用已有有界解析，直接打开可展开一至多曲及原生 range；osu! 定义解析到本地音源，实际音频仍走 C++ |
| 插件封装 | NCM、KGM、KGMA、VPR 与旧版一样属于 `echo.music-unlock` 插件路径；当前未迁该插件，扫描/直接打开明确报告 `audio_format_provider_required`，不把加密字节交给普通解码器 |

CUE/osu! 文件解析在后台运行，选取/展开最多 1000 项，保留稳定虚拟 track identity。队列和播放栏共用该解析器，避免 `.cue` 被误送入 FFmpeg 当音频文件。

## 本轮验证

使用当前 FFmpeg SDK 生成短本地夹具，音频输出音量为零，仅检查真实元数据、native 首帧和控制回执。

- 20 项已通过：WAV、FLAC、MP3、AAC、AAC/M4A、ALAC、Vorbis/OGA、Opus、WavPack、TTA、AIFF、AIFC、AU、VOC、W64、CAF、WMA、MP2、AC3、EAC3。
- 后续 9 项通过：TrueHD、DTS、MKA/FLAC、MKV/Opus、WebM/Opus、MOV/AAC、MP4/AAC、DSF/DSD64、RF64。
- 额外压缩 WAV/ADPCM 夹具检查真实 codec、元数据与原生首帧；此路径经过 native 回退，不能把容器名 WAV 当作 PCM codec。
- CUE 范围展开及原生片段播放、osu! 音源解析、插件依赖边界、无音轨视频拒绝通过。
- 53 项旧清单逐项比对；native 能力声明与托管 48 项普通格式清单比对。文件选择器、队列和扫描采用该一致定义。

TrueHD 初次使用短全静音夹具时自动识别失败，ffprobe 也同样拒绝；改用 1 秒有效信号夹具后通过。该夹具修正与“保留未知时长”的产品规则分别记录。前 20 项与后 9 项分批验证，未把不成功的运行写成整轮通过。

当前 SDK 的 decoder 列表确认包含 APE、TAK、Musepack SV7/SV8、MP1、Speex、AMR-NB/WB、DSD LSBF/MSBF；SDK 与播放宿主的 avcodec-62.dll SHA-256 一致。APE、TAK、Musepack、MP1、Speex、AMR、DFF 尚缺本轮真实样本验证，不能把 decoder 存在写成实播通过。插件封装、受保护 M4P、长音频/损坏媒体极端案例及跨平台包尚未验收。没有写入旧生产曲库、网络下载或发布。

最终桌面构建通过，0 错误，5 条并行窗口/视图缺少无参构造的既有 XAML runtime-loader 警告；本轮未进行动态 XAML 热加载或跨平台 UI 验收。与并行扫描 API 整理同步补齐了 SelectedFileDiscovery 的 osu suppressed 参数，保留单文件不枚举兄弟文件的语义。

## 构建和运行

Windows native 扫描器：`tools/build-native-scanner.ps1 -FfmpegRoot <FFmpeg shared development root>`。编译选项是本机 SDK 位置，不写死业务路径；没有配置 SDK 的构建仍可用原有基础解析，能力报告会注明 FFmpeg 探测不可用。FFmpeg DLL 随 scanner 放进当前平台的 `runtimes/<rid>/native`。

最小检查：`dotnet run --project tests/Echo.Formats.Smoke -- <ffmpeg.exe> <scanner> <audio-host> <legacy-root>`。`--remaining` 检查后 9 项；`--capabilities-only` 只核对已部署的 native 格式能力。构建和检查遵守 120 秒预算。
