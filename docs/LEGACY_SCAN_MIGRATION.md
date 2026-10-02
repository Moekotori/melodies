# 旧版扫描实现迁移

本轮以旧 ECHO 实际实现为行为依据，直接复用能独立编译的模块；TypeScript 业务按同一规则转为 C#。CUE parser 原样保留，osu 以所列规则和定向检查收口，其他旧行为继续逐项对照。

源版本：D:/ECHO，Git 3dbf28993cddf9f10ea355b88b0105abe85e5174。旧目录只读，生产数据库不参与开发验证。

## 实施计划

1. [已完成] 原样迁入 C++ library-scanner 和 C# ScanCueReader/ScanCueParser，记录来源与许可，保持新接口文件模块化。
2. [已完成] 迁入按文件夹的 CUE 展开/音频抑制、osu 元数据覆盖/共享音频去重、原速优先、pack 标签及谱面属性缓存；各缓存与解析输入有上限。
3. [已完成] 数据库 schema 5 保存来源与缓存、schema 7 保存物理音源索引；兼容并行加入的专辑 schema 6，不回退专辑/艺术家模块。
4. [已完成] 专辑队列入口通过现有 AudioSource.Range 接入 CUE 播放来源，保留旧版片段标识与新队列 occurrence。
5. [已短测] 17 项真实文件/SQLite 检查通过，含侧挂/内嵌 CUE、分段范围、整曲抑制/恢复、osu 共享音频/Unicode/BPM、缓存、来源变化、原速选择、损坏 CUE 保护和本工程构建的 C++ 自动查找。

边界：直接复用 parser 不等于整个旧版功能已完成。旧 Core 的分析身份、playlist、封面写入和 schema 语义仍需逐能力迁移；旧 schema 不直接覆盖到新库。回滚以匹配的代码与数据库备份为单位，禁止删除用户曲库。

## 已复用来源

- native/library-scanner 的初始源码复制到 native/vendor/echo-library-scanner；同工作区随后加入 FFmpeg metadata 回退等并行增强，当前 main.cpp 不再与初始 snapshot 完全一致。本轮已验证复制后构建的 v3 基础协议，不把并行新增能力算成全部已验收。
- core/Echo.Core/Media/ScanCueReader.cs 与 ScanCueParser.cs 及协议 DTO 原样保留在 Infrastructure/Legacy/Cue。
- OsuScanText、parseOsuFileMetadata、OsuFolderFileFacts、OsuRateMetadata、OsuSpeedVariants、OsuPackTags 对应独立 C# 文件；维持乱序章节、Unicode、编码回退、共享音频及有证据的速率合并规则。
- 新目录扫描分两次枚举当前文件夹，先处理 CUE/osu 定义，再按 128 条音频批次流式输出；不驻留整库。

## 构建与运行

运行 tools/build-native-scanner.ps1 构建复制的 scanner，再构建 CLI/桌面工程。产物随 Infrastructure 的 Content 项复制到 runtimes/<rid>/native，运行时无需旧 ECHO 路径；仍接受显式 --native-scanner 覆盖。

工作区名称含 #，CMake 无法把生成输出放在该路径内。本轮使用无 # 的临时构建目录后复制到 artifacts，MSBuild 报临时目录增量构建提示；构建成功，后续可切换到本机固定 cache。这个提示不代表代码编译失败。

CLI 与桌面文件夹导入均已装配 LegacyFolderDiscovery。普通无 ID3 MP3 保持旧版严格 native 规则，仅 osu overlay 开启 allowUntaggedMp3；完整 TS/TagLib 回退、RF64/BWF/复杂 WAV、osz 归档及全部 storyboard 数据仍需继续迁移，不宣称已经与旧版完整等价。

验证本机 Windows Release，17 项功能检查通过，累计构建/必要重跑约 40 秒；CUE 范围已进入实际播放 SourceRange，真实设备音质与三端运行未在本轮验收。桌面构建通过并报告 6 条 XAML runtime loader 构造器警告，本轮未修改这些并行视图。日志在 misc/legacy-scan-validation，不提交临时库或构建日志。
