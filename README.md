# melodies

**基于 Avalonia、C# 与原生 C++ 音频引擎的桌面音乐播放器。**

melodies 面向 Windows、macOS 和 Linux，提供本地曲库管理、音乐播放、歌词展示与音频处理。设计上强调模块边界、快速响应和有预算的资源使用。

> 项目仍在开发中，各平台的设备支持、Native AOT 发布和跨机器输出需要分别验证。

## 当前能力

| 方向 | 已接入内容 |
| --- | --- |
| 曲库 | 本地目录扫描、增量更新、SQLite 索引、分页浏览、搜索与筛选，以及 CUE / osu! 定义解析 |
| 播放 | 独立原生音频宿主、播放队列、暂停与跳转、设备配置、会话恢复和信号路径展示 |
| 歌词 | 本地歌词解析、逐字时间数据、多种绘制样式、全屏歌词和播放控制 |
| 浏览与外观 | 歌曲、专辑、艺术家、文件夹、歌单和历史页面，主题与自定义背景 |
| 音频处理 | DSP 机架、EQ、耳机校正及 SRC / SDM 配置；实际能力由原生宿主与设备确认 |
| 语言 | 简体中文、English、跟随系统，以及无需重启的用户 JSON 语言包 |
| 服务连接 | 独立 Core、本地多客户端连接、显式配对的远程控制和阶段性的网络 Output |

格式支持与限制见 [音频格式](docs/AUDIO_FORMAT_SUPPORT.md)。网络输出的当前路径与待完成事项见 [网络 Output](docs/NETWORK_OUTPUT.md) 和 [原生 PCM 数据面](docs/NATIVE_PCM_DATA_PLANE.md)；多房间同步尚未完成。

## 快速开始

### 环境

- **.NET 10 SDK**：版本以 [global.json](global.json) 为准。
- **Node.js / npm**：仅用于 `npm run dev` 开发入口，无需 `npm install`，播放器运行不依赖 Node.js。
- **对应平台的原生音频宿主及动态库**：播放依赖 `native/runtime/<RID>/` 下的运行库；原生适配与构建说明见 [原生宿主适配](docs/NATIVE_HOST_ADAPTATION.md)。

### 启动桌面

```sh
git clone https://github.com/Moekotori/melodies.git
cd melodies
npm run dev
```

命令会恢复 NuGet 依赖、构建并打开桌面窗口。首次运行需要联网恢复依赖；仅安装 .NET SDK 不代表原生播放运行库已就绪。

Debug 下，`.axaml` 视图、样式与资源字典由 [HotAvalonia](https://github.com/Kira-NT/HotAvalonia) 热更新；C# 修改由 `dotnet watch` 处理，不支持的修改会触发重建与重启。初始化或依赖装配变更后，可在终端按 `Ctrl+R` 手动重启，按 `Ctrl+C` 停止开发进程。重启后不保证保留界面临时状态。

不使用热更新时：

```sh
dotnet run --project src/Echo.Desktop -c Release
```

### 扫描与查询

CLI 的普通业务命令同样通过 Core 工作。下面使用独立的开发数据库；将音乐目录替换为实际路径：

```sh
# 扫描目录
dotnet run --project src/Echo.Cli -c Release -- scan --db misc/library.sqlite --root "音乐目录"

# 分页列出或搜索歌曲
dotnet run --project src/Echo.Cli -c Release -- list --db misc/library.sqlite --limit 100
dotnet run --project src/Echo.Cli -c Release -- list --db misc/library.sqlite --search "歌名"

# 查看统计与某次扫描的问题
dotnet run --project src/Echo.Cli -c Release -- stats --db misc/library.sqlite
dotnet run --project src/Echo.Cli -c Release -- issues --db misc/library.sqlite --job "扫描返回的 jobId"
```

扫描默认增量执行。`--force` 可复查属性未变的文件；`--detect-missing` 显式启用缺失文件清理，枚举不完整、解析错误或取消会阻止清理。原生 scanner 的构建和兼容范围见 [扫描兼容说明](docs/LEGACY_SCAN_MIGRATION.md)。

开发数据库和临时文件放在 Git 忽略的 `misc/` 中，请使用独立测试库运行以上示例。

## 架构

```text
Desktop / CLI（Control）
          │ 类型化命令、查询与状态订阅
          ▼
      Core.Host ───────── Bridge.Host
          │               协议网关
          ├─ 曲库、索引、扫描与后台任务
          ├─ 歌单、历史、队列与播放会话
          └─ 独立 Native Audio Host
                       解码、DSP、设备输出与播放事实
```

Core 持有业务状态和资源生命周期，Control 负责交互与有界展示状态。实际播放位置、设备配置和输出结果来自原生音频宿主确认。关闭控制窗口不等于停止 Core 已接受的任务或播放。

`Bridge.Host` 当前承担协议网关职责，独立设备端另由 `Output.Host` 接入。网络音频数据面的迁移仍在进行，不能据此视为完整的桥核分离或分组同步实现。职责、部署入口和验证边界见 [Core / Control / Bridge](docs/BRIDGE_CORE_SEPARATION.md)。

### 代码目录

| 路径 | 职责 |
| --- | --- |
| `src/Echo.Desktop` | Avalonia 界面、展示状态与桌面装配 |
| `src/Echo.Core*` | Core 服务、公开契约、类型化客户端与传输 |
| `src/Echo.Bridge*` / `src/Echo.Output*` | 协议网关与独立输出端接入 |
| `src/Echo.Application` / `src/Echo.Playback*` | 业务协调、播放队列与会话编排 |
| `src/Echo.Contracts` / `src/Echo.Infrastructure` | 基础业务契约及存储、文件、元数据等适配 |
| `src/Modules` | 专辑资料、DSP 等功能模块 |
| `src/Echo.Audio.*` / `native/` | 原生音频协议、客户端、宿主源码与平台运行库 |
| `tests/` / `tools/` | 聚焦验证、预览与开发工具 |

## 开发文档

- [开发约定与性能预算](docs/ECHO_AVALONIA_DEVELOPMENT.md) · [仓库工作规则](AGENTS.md)
- [扫描与数据库](docs/SCANNING_DATABASE.md) · [扫描迁移与兼容](docs/LEGACY_SCAN_MIGRATION.md)
- [原生音频适配](docs/NATIVE_HOST_ADAPTATION.md) · [音频格式](docs/AUDIO_FORMAT_SUPPORT.md)
- [歌词界面](docs/LYRICS_MIGRATION.md) · [本地歌词兼容](docs/LYRICS_LOCAL_COMPATIBILITY.md)
- [ECHO 功能对照与歌单补齐](docs/ECHO_FUNCTIONAL_PARITY.md)
- [远程 Control](docs/REMOTE_CONTROL.md) · [网络 Output](docs/NETWORK_OUTPUT.md)
- [语言包与国际化](docs/I18N.md) · [主题系统](docs/THEME_SYSTEM.md)

参与开发前请阅读工作规则。新增业务明确所属模块和生命周期，保持原生音频的独立进程边界；按改动范围执行构建、聚焦测试和必要运行检查。构建或模拟测试通过不能替代真实音频设备、跨平台与发行物验证。

## 许可证

除另有明确声明的第三方代码、依赖和资源外，本项目采用 **GNU Affero General Public License v3.0（AGPL-3.0-only）**，完整文本见 [LICENSE](LICENSE)。

第三方内容保留各自许可证与版权声明，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 及对应目录中的许可文件。
