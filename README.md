# melodies

Avalonia 桌面重写的新工作区。当前提供扫描、独立 SQLite 数据库、分页查询及 CLI，并已接入桌面界面和原生播放；完整旧版功能与旧库迁移尚未完成。

开发约束见 [开发文档](docs/ECHO_AVALONIA_DEVELOPMENT.md)，已实现能力及边界见 [扫描和数据库模块](docs/SCANNING_DATABASE.md)。

默认桌面与 CLI 通过独立 Core 服务工作，连接服务由按需启动的 Bridge 宿主拥有；架构、入口及网络输出边界见 [桥核分离](docs/BRIDGE_CORE_SEPARATION.md)；显式配对的远程控制见 [远程 Control](docs/REMOTE_CONTROL.md)，独立设备端与只读媒体链路见 [网络 Output](docs/NETWORK_OUTPUT.md)。

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

WAV/FLAC 使用有界解析，CUE 与 osu 文件夹规则已迁入。运行 tools/build-native-scanner.ps1 后，MP3、M4A、Ogg、Opus、AIFF 等可自动使用本工程构建的旧版 C++ scanner，也支持 --native-scanner 覆盖。完整兼容范围见 [旧版扫描迁移](docs/LEGACY_SCAN_MIGRATION.md)；完整旧库导入仍在后续阶段。

普通扫描是增量扫描。需要复查属性未变但内容被改写的文件时加 `--force`。完整扫描需要标记删除文件时显式加 `--detect-missing`；任何枚举不完整、解析错误、链接排除或取消都阻止缺失清理。

数据库只接受本项目独立格式，拒绝直接改写旧 ECHO 生产库。测试与临时文件放在 Git 忽略的 misc/。

## 显示语言

设置 → 通用 → 显示语言支持简体中文、English 和跟随系统，修改即时生效并保存。用户可在同页“用户语言包”中导入 JSON、导出模板、打开目录和重新加载；新语言或内置文案覆盖无需构建或重启。随发行版提供的内置包仍放在 `src/Echo.Desktop/Resources/Languages/`。资源格式、开发调用和当前迁移边界见 [I18N 文档](docs/I18N.md)。
