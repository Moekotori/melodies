# 第三方依赖

## QPlayer AutoMix Alpha

从用户自有 ECHO 仓库 `moekotori/echosteam` 的 `src/main/audio/automix-alpha` 和现有原生 Alpha DSP 迁移。
上游为 QPlayer 1.0.0（`dev.t1m3.qplayer`），更新包 `automix (1).zip`（2026-10-19），
SHA-256 `55c956b397cfb10b88bcc35a560f14acd5119b29d3cb6819a40f156dcb4816c4`。
更新版采用 Apache-2.0；完整许可见 `licenses/QPlayer-AutoMix-Apache-2.0.txt`。
保留 ECHO 的来源记录：提供者允许修改、公开集成及商业分发，许可包含随附 EQ 参数。
本次将频谱材料、AutoCue 候选与评分、MaterialProfile、Chooser、RouteGates／RouteScore、
EqFeatureExtractor 和完整 MixEqMlp 权重移植到 C#，复用原生 C++ 六路线 DSP。
改造包含 Core 所有权、有界缓存／串行分析、取消和宿主回执检查；权重数值保持不变。
不引入 Java／JVM、JAR、JCodec 或源媒体资源。分发时保留本记录及完整许可。

## 其他依赖

HotAvalonia 3.1.5：仅 Debug 开发热更新使用，MIT，来源 https://github.com/Kira-NT/HotAvalonia 。运行时 XAML loader 使用与框架一致的 Avalonia.Markup.Xaml.Loader 12.1.3；两项直接引用均不进入 Release 依赖图。开发包的传递依赖以 NuGet 恢复清单为准。

Microsoft.Data.Sqlite 10.0.11：Microsoft 官方 .NET SQLite provider，MIT，来源 https://github.com/dotnet/efcore 。其 SQLitePCLRaw 与 e_sqlite3 依赖及 native runtime 在发布前逐项生成实际包清单和 notices；本轮不制作发行包。

旧 ECHO scanner 与 CUE 源码：按用户要求直接复用 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174 的实现，AGPL-3.0；原 C++ snapshot 和 LICENSE 在 native/vendor/echo-library-scanner，CUE 来源见 Infrastructure/Legacy/Cue/ORIGIN.md。派生 osu C# 实现保留对应源文件指引。构建产物在 artifacts 并按需复制到输出目录，正式分发仍需实际源清单与 notices。
