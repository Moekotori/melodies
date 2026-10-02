# 第三方依赖

HotAvalonia 3.1.5：仅 Debug 开发热更新使用，MIT，来源 https://github.com/Kira-NT/HotAvalonia 。运行时 XAML loader 使用与框架一致的 Avalonia.Markup.Xaml.Loader 12.1.3；两项直接引用均不进入 Release 依赖图。开发包的传递依赖以 NuGet 恢复清单为准。

Microsoft.Data.Sqlite 10.0.11：Microsoft 官方 .NET SQLite provider，MIT，来源 https://github.com/dotnet/efcore 。其 SQLitePCLRaw 与 e_sqlite3 依赖及 native runtime 在发布前逐项生成实际包清单和 notices；本轮不制作发行包。

旧 ECHO scanner 与 CUE 源码：按用户要求直接复用 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174 的实现，AGPL-3.0；原 C++ snapshot 和 LICENSE 在 native/vendor/echo-library-scanner，CUE 来源见 Infrastructure/Legacy/Cue/ORIGIN.md。派生 osu C# 实现保留对应源文件指引。构建产物在 artifacts 并按需复制到输出目录，正式分发仍需实际源清单与 notices。
