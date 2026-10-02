# ECHO Avalonia

Avalonia 桌面重写的新工作区。当前提供扫描、独立 SQLite 数据库、分页查询及 CLI，并已接入桌面界面和原生播放；完整旧版功能与旧库迁移尚未完成。

开发约束见 [开发文档](docs/ECHO_AVALONIA_DEVELOPMENT.md)，已实现能力及边界见 [扫描和数据库模块](docs/SCANNING_DATABASE.md)。

## 使用

### 桌面开发与热更新

需要 Node.js/npm 和本项目锁定的 .NET 10 SDK。在工作区根目录运行：

```powershell
npm run dev
```

命令会构建并打开 Avalonia 桌面窗口，无需 `npm install`。首次启动会由 .NET 恢复 NuGet 依赖。

Debug 使用 [HotAvalonia](https://github.com/Kira-NT/HotAvalonia) 监听 `.axaml`，保存视图、样式和资源字典后在运行中的应用重新加载；运行时 XAML 错误需要修正后重新保存。C# 修改由 `dotnet watch` 应用支持的热更新；不支持的修改自动重建并重启，重启不保证保留界面临时状态。初始化、构造函数或依赖装配修改可能需要在终端按 `Ctrl+R` 手动重启。按 `Ctrl+C` 停止开发进程。

热更新依赖只用于 Debug，Release 保持原有 AOT 兼容检查。Node/npm 仅提供开发命令入口，不属于播放器运行依赖。

需要 .NET 10 SDK。在此目录执行：

```powershell
dotnet run --project src/Echo.Cli -c Release -- scan --db misc/library.sqlite --root '音乐目录'
dotnet run --project src/Echo.Cli -c Release -- list --db misc/library.sqlite --limit 100
dotnet run --project src/Echo.Cli -c Release -- list --db misc/library.sqlite --search '歌名'
dotnet run --project src/Echo.Cli -c Release -- stats --db misc/library.sqlite
dotnet run --project src/Echo.Cli -c Release -- issues --db misc/library.sqlite --job '扫描返回的jobId'
```

WAV/FLAC 使用内置有界解析。MP3、M4A、Ogg、Opus、AIFF 等通过 `--native-scanner` 指定已构建的 ECHO C++ scanner；不支持或损坏的文件会计入失败，不生成伪造 metadata。封面提取、CUE/osu 虚拟曲目和完整旧库导入在后续阶段迁移。

普通扫描是增量扫描。需要复查属性未变但内容被改写的文件时加 `--force`。完整扫描需要标记删除文件时显式加 `--detect-missing`；任何枚举不完整、解析错误、链接排除或取消都阻止缺失清理。

数据库只接受本项目独立格式，拒绝直接改写旧 ECHO 生产库。测试与临时文件放在 Git 忽略的 misc/。
