# 第三方依赖

HotAvalonia 3.1.5：仅 Debug 开发热更新使用，MIT，来源 https://github.com/Kira-NT/HotAvalonia 。运行时 XAML loader 使用与框架一致的 Avalonia.Markup.Xaml.Loader 12.1.3；两项直接引用均不进入 Release 依赖图。开发包的传递依赖以 NuGet 恢复清单为准。

Microsoft.Data.Sqlite 10.0.11：Microsoft 官方 .NET SQLite provider，MIT，来源 https://github.com/dotnet/efcore 。其 SQLitePCLRaw 与 e_sqlite3 依赖及 native runtime 在发布前逐项生成实际包清单和 notices；本轮不制作发行包。

本轮没有复制既有 C++ scanner 源码或二进制到新仓库。可选原生 adapter 通过用户指定的既有 ECHO scanner 执行受限 metadata 请求。正式随应用打包该组件前需要纳入其源版本、许可证和构建清单。
