# WAV 扫描兼容性

本页描述 Core 侧曲库元数据扫描，不等同于播放、音频帧完整性或设备输出验收。实现位于 `Echo.Infrastructure/Metadata`，由 Core 扫描工作器持有；Control 和 Bridge 不运行解析器。

## 当前路径

| 结构或编码 | 扫描行为与证据 |
| --- | --- |
| RIFF PCM 8/16/24/32 位、float 32/64 位 | 直接读取格式/标签；实际编码文件通过 |
| PCM 64 位整数 | 格式头夹具通过；未做实际编码/播放验证 |
| WAVEFORMATEXTENSIBLE PCM/float | 校验 GUID、cbSize、容器与有效位深；20 位存于 24 位、24 位存于 32 位及 6/8 声道夹具通过 |
| RF64/BW64 | 读取 ds64 的 64 位 RIFF/data 大小及扩展块表；同名表项按次序消费。超过 4 GiB 的稀疏 RF64、data 后 INFO、BW64 bext 夹具通过；另有实际编码 RF64 |
| RIFX | 按大端读取块大小、格式和 INFO；PCM 元数据明确标记 PCM_BE，实际音频封装通过。未宣称大端 Extensible/压缩编码或播放链路支持 |
| IMA/MS ADPCM、A-law、μ-law、MP3-in-WAV | 原生 FFmpeg 探测，实际编码文件通过；不用 PCM 时长公式处理压缩块 |
| 压缩 Extensible | 交给原生确认子编码；实际 G.711 A-law 封装通过，不将 WAVE_FORMAT_EXTENSIBLE 当成音频编码。其他子编码取决于部署的解码器 |
| DTS/IEC61937 伪装 PCM | 16 位立体声固定前缀嗅探后交给原生；实际 DTS 和 IEC61937/AC3 封装确认到 DTS/AC3 |
| Sony Wave64 | 独立 .w64 原生路径，实际编码文件通过 |
| .wave/.bwf 扩展名 | 与 .wav 共用托管快路径，实际文件通过 |
| WAV ID3 | 原生读取；实际 PCM + ID3v2.3 UTF-16 标题通过 |

原生 WAV 回退必须经过实际编码探测；探测失败明确报告，不保留未经确认的 PCM/Extensible 容器猜测。普通 WAV 仍走托管快路径。

## 标签与编码

INFO 读取 INAM/TITL、IART、IPRD/IRPD、IGNR/GNRE、ICRD/YEAR、ITRK/IPRT；标准字段优先于别名。支持 UTF-8/BOM、UTF-16 BOM、含明显交替零字节的无 BOM UTF-16，以及显式 CSET 代码页；CSET 位于 INFO 之后也生效。没有声明的旧编码用严格 GB18030/Shift-JIS 与西文回退，中文和日文代表样例通过；没有声明的历史编码始终可能存在歧义。

bext 仅读取固定前缀内的 description、originator、origination date，作为标题、歌手、年份的缺省来源；普通 INFO 非空字段优先。UMID、时间参考、响度、iXML/axml 等专业字段尚未映射到曲库模型，不把完整广播元数据支持作为已完成功能。

## 资源与升级

按 RIFF/ds64 声明的边界遍历，最多 1024 个块、1024 个 ds64 表项。INFO 最多累计 256 项，相关单字段最多 16 KiB，累计读取相关标签/bext 不超过 256 KiB；图片、未知块和音频正文通过 seek 跳过。PCM 伪装检测仅额外读取最多 64 字节，不解码音频，也不保证找到后移很远的同步头。超预算/截断文件报告单项失败；取消在块、标签及探测前检查。原生回退沿用独立子进程与 16 MiB I/O、探测/请求超时预算。

WAV/WAVE/BWF 元数据解析版本为 4，其他格式保留版本 3。下次正常扫描只重新读取受影响的 WAV，包括以 WAV 为物理音源的 CUE/osu；已更新 WAV 再次扫描复用指纹，不要求整库 force，无数据库 schema 变更。

重复 fmt/data、wavl、非标准 GUID 等复杂结构只进入原生尝试，不承诺所有历史 WAV 变种均可导入。当前部署的 FFmpeg 对部分压缩 Extensible（例如使用 SamplesPerBlock 的 ADPCM）和 RIFX 扩展仍有能力边界；不能只因扩展名为 WAV 就视为已支持。

## 2026-10-19 Windows Release 验证

- `Echo.Library.Smoke wave-compatibility`：48 项检查通过，含超过 4 GiB 的稀疏 RF64、ds64 表、边界/位深/对齐错误、标签编码、真实 SQLite 选择性升级与坏文件隔离。
- `Echo.Formats.Smoke --wave-only --metadata-only`：20 种实际编码/封装元数据扫描通过，全程未打开音频设备。
- 原有 CUE 40 项、scan-coverage、legacy 23 项通过；Core.Host Release 构建零警告零错误。原生 scanner 构建成功，MSBuild 提示临时构建目录 MSB8029；未更改参考仓库。

尚无真实生产大库、NAS、三平台、长期进程树内存或真实音频设备验收。

格式约束依据：[Microsoft WAVEFORMATEXTENSIBLE](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ksmedia/ns-ksmedia-waveformatextensible)、[EBU RF64](https://tech.ebu.ch/files/live/sites/tech/files/shared/tech/tech3306v1_0.pdf)、[EBU BWF](https://tech.ebu.ch/docs/tech/tech3285.pdf)。RIFX 回退的观察同时对照了 [FFmpeg RIFF 解析源码](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/riffdec.c)。
