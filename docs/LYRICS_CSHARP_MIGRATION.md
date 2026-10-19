# 歌词视觉 C# 迁移

## 当前状态

2026-10-19：磁带场景已经切换到纯 C#。JIZURA、Blossom 与歌词背景仍使用脚本，整体迁移尚未完成，不能把本阶段称为全部歌词视觉已原生化。

本阶段只涉及 Control 的视觉场景，不改变 Core、播放事实、曲库、歌词解析、设置与 UI 交互。原 `SourceSceneWorker` 的单工作器、容量 1 请求队列、单槽点击、隐藏/退出释放和确认时钟保持原样。

## 模块与资源

- `Echo.Visuals.Native`：不引用 Jint 或 Desktop；持有 C# 磁带场景、字体缓存、纹理、Skia picture 与原着色器/图片。可独立 Native AOT 编译。
- `Echo.Visuals.SourceLyricsEngine`：保留原调用契约。磁带分支创建 `CassetteEngine`，不会创建脚本解释器或 Canvas 脚本池。Blossom 沿用原路径。
- 磁带按输入、时间轴、布局、逐字运动、行栅格、标题、粒子、场景组合、纹理与绘图分别维护。共享字体缓存迁入 Native 模块，脚本 Canvas 也复用同一实现。
- 旧 `cassette.js`、磁带 TS 入口和传输适配器已删除；导入工具只导入磁带图片和着色器，不再生成磁带 JS。出处说明随资源保留。

纹理限制为 48 张/64 MiB；行缓存最多 6 项；同时显示最多 4 行；单帧最多栅格化 2 条新行。100 个粒子预分配，实例缓冲随行复用。指针水波最多 3 条、合成水波最多 8 槽；字体缓存最多 64 种样式、每种 12 个字体。布局/字素栅格只在缓存缺失时生成，不逐帧解析/序列化 JSON。资源由单个场景 owner 释放。

## 已验证

迁移前用原实现保存同一 fixture 的画面和点击区域，基线程序与输出保存在忽略目录 `artifacts/lyrics-csharp/`。测试入口是 `tests/Echo.Visuals.MigrationSmoke`。

- Release 模块构建通过。
- 45 帧静态对照：昼/夕/夜 × 1440×900、800×1200、3840×2160 × 5 个时间点。覆盖封面、翻译、逐词时间、英文换行、组合字符和 emoji；点击命中网格一致。
- 8 帧昼间动态对照：入场、逐词扫光、散场、磁带转动、暂停时钟修正和指针水波。点击命中网格一致。
- 像素大部分完全相同；静态帧的少数帧最多 1–7 个通道值相差 1，动态对照一帧有 2 个通道值相差 1。测试只容许单通道误差 ≤1，且平均误差 ≤0.0001/255，不容许几何/点击差异。
- 重复 Build、覆盖封面、切换场景 owner、停止动效、取消及重复释放通过。生产资源不再包含磁带 JS。
- 共享字体与脚本宿主清理后的 Blossom 回归：6000/6001/12000 ms 三张 PNG 与迁移前二进制输出 SHA-256 完全一致。原预览工具固定坐标的 Blossom seek 断言在旧、新二进制中均返回 -1，而非预期 4400 ms；该既有断言未修改。
- `tests/Echo.Visuals.NativeSmoke` 仅引用实际生产 Native 模块，Windows x64 Native AOT 发布无警告，并实际执行昼/夕/夜的 Skia 绘制、封面、暂停/动效、取消及释放。

同机相同 fixture、稳定帧记录阶段的托管分配：旧实现约 276–323 KB/帧，C# 约 11–15 KB/帧。短测的记录耗时由约 0.8–2.3 ms/帧降至约 0.07–0.19 ms/帧。这里统计调用线程和 picture 记录，不包含最终 raster/GPU 合成、原生堆及整棵进程树驻留，不能据此声称总内存或真实显示帧率下降相同比例。

## 验证命令

```powershell
dotnet run --project tests/Echo.Visuals.MigrationSmoke -c Release -- --lifecycle
dotnet run --project tests/Echo.Visuals.MigrationSmoke -c Release -- artifacts/lyrics-csharp/reference --verify
dotnet run --project tests/Echo.Visuals.MigrationSmoke -c Release -- artifacts/lyrics-csharp/reference --verify --motion-only
dotnet publish tests/Echo.Visuals.NativeSmoke -c Release -r win-x64 -p:PublishAot=true -o artifacts/lyrics-csharp/native-aot
artifacts/lyrics-csharp/native-aot/Echo.Visuals.NativeSmoke.exe artifacts/lyrics-csharp/aot-frames
```

画面对照需要迁移前基线；不要使用待测实现重新生成基线后宣称保真通过。生命周期和 Native AOT 检查不依赖这份本地基线。

## 验证边界与后续迁移

- 当前 Desktop 整体构建被 `CoreLibraryClient` 缺少 `IArtistLibrary.GetArtistInsightsAsync` 和 `IArtistInformationClient.GetArtistInformationAsync` 实现阻断；这些文件未在本阶段修改。没有绕过接口或修改无关业务来完成构建。
- 原有混合 `Echo.Visuals` 仍引用 Jint，整体 AOT 发布会出现 Jint 裁剪/动态加载错误；纯 C# Native 模块已独立发布验证。这不代表整应用 AOT 已通过。
- 未验证 macOS/Linux 产物、实际桌面页面运行、所有歌词种子、昼夜自动过渡的逐帧比较、随机粒子逐点比较及长期进程树内存。随机粒子的池大小、生成分布和运动公式已对应迁移，基线没有固定原 JS 随机序列。
- 后续必须逐一迁移 JIZURA、Blossom、背景引擎，每条路径保留原效果/交互对照后再删除脚本；不能用近似重绘代替源算法，也不能只把脚本改扩展名或嵌入 C# 字符串称为迁移。
