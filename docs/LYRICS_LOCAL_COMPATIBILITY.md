# 本地歌词兼容增量

日期：2026-10-02。只读对照旧版 `D:/ECHO/src/main/lyrics/extendedLyricsFormats.ts`、`localLyricsSidecars.ts`、`lyricsSidecarLanguage.ts`、`embeddedLyricsTracks.ts` 与 `lyricsParser.ts`。

## 已接入

- QRC 后置词时间、XML `LyricContent` 容器及绝对 / 相对时间判断；KRC 前置相对词时间、`krc1` XOR + zlib 文件和内置翻译 / 罗马音。
- LRC 中混存的 QRC / KRC 格式、冒号小数时间戳，重复时间戳行的逐字时间平移；原有 LRC、增强 LRC、YRC、TTML、SRT、VTT、TXT 保留。
- 同目录和 `lyrics/` 子目录；`Song.lrc` 与 `Song.mp3.lrc`。当前优先级为 TTML、YRC、QRC、KRC、LRC、SRT、VTT、TXT；先同目录，再子目录，每个目录先不带音频扩展名。
- `.t` / `.r` 伴随文件及 `.zh`、`.en`、`.rom`、`.pinyin` 等语言后缀，支持点 / 下划线 / 横线分隔。没有普通主文件时可提升语言文件作为主歌词。
- 伴随文件扣除自身 offset，再二分对齐主轨播放时间；容差最多 1000 ms，且不超过相邻主行距离的一半，避免把缺失译文错贴到密集的下一句。纯文本只在整轨行数相同时按行配对。嵌入副文本优先，外部文件补空缺。
- UTF-8、UTF-16 / UTF-32 BOM 和无 BOM 的 GB18030 回退。损坏的 BOM 文件不会重新解释成旧编码。
- 自动发现跳过空文件及无法读取 / 解码的候选；显式导入显示错误。损坏的可选伴随文件或 KRC language 元数据保留主歌词。
- 歌词页和首页使用复合文档解析入口，文件选择器开放 QRC / KRC。首页按音频路径读取导入选择，修复与歌词页保存键不同的问题。

不修改音频目录、数据库或旧 ECHO 工作区，不增加网络请求、全库缓存或音频时钟 owner。

## 职责与资源

| 文件 | 职责 |
| --- | --- |
| `LyricsDocument.cs` | 主文本及最多两份伴随文本契约 |
| `LyricsParser.cs` / `TimedLyricsParser.cs` | 纯解析、格式识别和时间标准化 |
| `LyricsCompanionMerger.cs` | 按实际播放时间配对副文本 |
| `LocalLyricsSource.cs` | 唯一读取准入、候选选择和取消 |
| `LocalLyricsReader.cs` | 有界文件 / KRC 解压与编码读取 |
| `LocalLyricsSidecars.cs` | 有界命名发现，不递归目录 |

一个 source 同时执行一个读取请求。主文件和最多两份伴随文件各不超过 1 MiB 字节、512 KiB 字符；KRC 解压输出另受 1 MiB 上限约束。每份文档最多 6000 行，普通单行最多 4096 字符；KRC JSON 深度最多 16，副文本单行最多 4096 字符。

读取、候选、目录枚举、逐行解析和副轨合并检查取消。XML / JSON 一次解析受大小限制，未宣称可中断解析器内部操作。语言别名每个目录最多检查 2048 个文件，范围外别名可能需要手动导入；固定 `.t` / `.r` 和普通主文件探测不受此枚举范围影响。原始文本和临时副轨随请求释放，UI 保留合并后的当前文档。

## 验证状态

`tests/Echo.Lyrics.Compatibility.Smoke` 已通过原有解析 smoke 和真实文件增量：两类命名、子目录、语言文件提升、独立 offset、稀疏译文、QRC XML / 两类词时间、KRC 真实压缩加密 / 语言轨道、GB18030 / UTF-16、自动回退、显式错误、解压上限、可选文件损坏、取消和多时间戳行数上限。

`tests/Echo.Lyrics.Smoke --local-compatibility` 已加入加载 / 渲染、双 offset 跳转、导入 / 再打开、错误恢复、过期结果和释放短测，本轮尚未成功执行，不能宣称 UI 验收通过。

桌面首次构建被歌词底栏并行接口中间状态阻断；接口随后收敛，UI 构建遇到歌曲页 `Artwork` / `Player` 重复声明。为保留生产源码，仅在 `misc/lyrics-compatibility/` 验证快照中去除相同声明，后续仍遇到电台页缺失 `RadioView`，停止追加重跑。完整应用编译、真实歌曲 / 设备 / 窗口与跨平台待验收。本轮累计约 36 秒，日志和预算在 `misc/lyrics-compatibility/`。

## 后续与回滚

未迁移旧版 ASS / SSA、TRC、LRCX、SNC、AWLRC 容器、音频内嵌标签、在线提供商、编辑器，以及 TTML 对唱 / 背景人声等丰富语义；既有样式也不因此成为旧 Canvas 引擎的等价实现。

回滚移除 QRC / KRC 分支、复合文本调用及新增本地读取能力，保留既有 UI、设置、扫描和播放会话 owner。
