# 歌词背景同步（2026-10-20）

范围属于 Control 展示层与 Echo.Visuals。背景选择是本机展示偏好；不增加 Core 业务、音频 owner、分析器、网络连接或生产数据库访问。

## 来源与覆盖

只读参考 `/Users/moekotori/echo`（echosteam，a44881fcc），使用原版 renderer、调色板、驱动、GLSL 及原始几何图片。`tools/Import-SourceLyrics.cjs <reference-checkout>` 生成本仓库的脚本资源和来源清单，不修改参考仓库。源码来源及哈希见 `src/Echo.Visuals/Resources/SourceLyrics/manifest.json`；图片来源说明保留在资源目录 README。

2026-10-20 从 echosteam `542e7c66c` 的只读检出同步六个像素背景：显像管、像素跃迁、翻牌浪潮、拼图归位、像素雨、均衡器城市。运行 `node tools/Import-SourceLyrics.cjs <reference-checkout> --background-only` 只更新背景脚本、图片和来源清单，不重导入 Blossom 或磁带资源。

背景目录按原版顺序提供 26 项：跟随样式、涟光、跟随主题、跟随封面、封面流动、流光、几何氛围，以及其余 shader 背景和自定义图片。共 22 个动态 renderer。Blossom、Cassette、Afterglow 独立舞台保留自己的背景 owner；普通歌词选取独立背景后保留前景布局，并替换原有背景装饰。遮罩、深浅主题与文字 / 控制条前景色按原版选择规则应用。

入口：歌词设置 → 歌词样式 → 背景样式。动态背景的帧数上限可输入 10–240 FPS，默认 60，随原有本机歌词偏好自动保存；失焦时最多 30 FPS。原版静态缩略图按需载入，不为选项运行场景。自定义图片支持 EXIF 朝向、模糊、亮度与不透明度，偏好可保存恢复。

## 原生适配与预算

- 原版脚本在单一串行工作器运行；macOS 使用系统 JavaScriptCore，其他平台复用已有 Jint。没有 WebView、DOM、任意文件或网络接口。
- 原版全屏 GLSL 适配 Skia RuntimeEffect：保留场景数学和颜色逻辑。SkSL 不支持的位操作、常量数组和可变全局索引改为有界等价表达式；halftone / prism 导数使用解析梯度，contour 使用一像素差分，因此抗锯齿仍可能与 WebGL 不同。
- 显式 textureLod 使用实际七级 mipmap 与级间采样，保留原版封面模糊层级。最多 2 个 program、4 张纹理、含 mipmap 的纹理总预算 16MiB；shader 输出限制 1920×1080。原版几何素材在固定尺寸解码。
- 背景请求通道容量 1、替换旧帧，点击单独保留最近一次；结果带 revision，丢弃迟到结果。显示图片与工作器帧均有明确释放路径。
- 动态背景使用 `TopLevel.RequestAnimationFrame` 跟随呈现周期，以小数截止时间落实配置上限；同一场景最多一个待执行回调，卡顿后跳过旧帧，不追赶积压。高帧率配置不再被原有 60 FPS 定时器截断，其他歌词舞台的帧策略保持原样。减少动效或静态图片停止动画驱动，隐藏时释放场景与封面数据。
- 流光、几何和均衡器城市按需读取现有权威频谱快照；其他 shader 保持原版环境驱动。均衡器城市静音稳定后报告 idle，停止请求和重绘并保留最后画面，播放恢复或交互重新唤醒。idle 回执带活动代次，不能覆盖后来的唤醒。
- 背景先在原版像素预算内绘入可复用 Skia surface，再放大到窗口，避免高 DPI / 4K 下直接放大着色器计算量；歌词前景不降采样。渲染操作引用计数保护 surface 与快照，隐藏、离开和最终操作释放后回收；uniform 数组复用，不逐帧重新创建。
- 封面通过已有有界异步 artwork lease 编码器捕获，单份编码 ≤4MiB、解码 ≤1M 像素，再裁剪为 512²。自定义文件 ≤32MiB、解码预算 ≤16M 像素、驻留长边 ≤1600；调整滑块复用同一图片，不重复读盘。

## 聚焦验证与已知边界

- 16 个原版动态 renderer 均实际编译、调用并输出原生图片。
- 原版浏览器与原生 640×360 同内容、同初始时间、同尺寸的并排对照覆盖涟光、几何氛围、封面流动、网点、广度优先。参见根目录 `design-qa.md` 和忽略目录 `misc/lyrics-background-sync/final/`。
- 实际 Avalonia 检查覆盖独立背景选择、原版缩略图、设置抽屉、自定义图片、暗色主题、减少动效、隐藏释放、恢复、独立舞台互斥和偏好保存。主工作区 Release 构建通过；已有 XAML runtime-loader 提醒不属于背景错误。
- macOS arm64 Native AOT 发布成功，并实际绘出涟光、几何氛围和广度优先背景。
- 尚不支持原版自定义 MV / 视频、创意工坊导入及每个 shader 的全部高级参数。当前同步默认场景和图片参数。Skia / WebGL 的采样、抗锯齿存在细微差异，不宣称逐像素相同。Windows / Linux 实际设备与全部动画帧尚未验收。

新增像素背景的聚焦入口：`dotnet run --project tools/Echo.BackgroundPreview -c Release -- misc/background-sync --pixel-smoke`。覆盖六个 shader 的编译与 320×180 绘制、120/240 上限传递、切换、封面、指针及均衡器静音/恢复；源帧记录耗时与分配不包含最终 GPU replay，不能当作真实窗口 FPS。独立设置、分数帧率节拍与隐藏恢复入口为 `tests/Echo.Lyrics.Smoke` 的 `--background-settings`，以单语言夹具隔离其他语言包的并行修改。

本次短测已完成六个 shader 绘制、静音恢复、10/37/120/144/240 设置输入到场景的传递、分数节拍及停顿后不追帧检查，并取得设置截图与 `BackgroundFrameRate: 240` 的实际偏好文件。完整隐藏恢复脚本在验证预算内未跑完；真实 GPU、高刷显示帧率与整棵进程树驻留尚未验收。

## 连续动态对齐补充

2026-10-19 补充逐帧检查，证据位于 `misc/lyrics-background-sync/motion/`。16 个内置动态背景分别以暗色 / 浅色运行 241 帧固定 30fps 输入，保存 0、1、4、8 秒的原版与原生画面。每张 `compare-*.png` 上行为原版、下行为原生，单帧 320×180；未将静态首帧当作动态验收。音乐响应另外对齐相同的有界 32 频段 fixture，在第 6 秒暂停；切歌 / 主题另检查 4.0、4.1、4.4、5.0 秒中间态。此为确定性展示输入，不代表真实音频设备或实时性能验收。

本轮修正：

- 帧驱动遵循窗口全局帧率预算；失焦或降低预算时同步更新原版质量 governor 的目标帧间隔，避免将主动降帧误判为 GPU 迟到。
- Cover Flow 时间增量与原版一致，单次最多 100ms。取色改为原图直接缩小到 64²，保留整个非正方形封面，不使用 shader 的裁剪 / mipmap 流程。
- 相同渲染器与 shader 家族的调整保留原始实例和动画相位；切歌不重建场景。封面 shader 使用不同 cover key，恢复原版 0.7 秒混合；Cover Flow 使用原版 1.2 秒颜色过渡。
- 新帧准备期间继续展示上一帧；revision 仍丢弃迟到结果，不因异步取图闪空。
- 接通指针移出并排除按钮 / 表单点击；捕获指针超出画面也走原版 leave。不会拦截歌词 seek 或控制条操作。
- 音乐驱动按权威频谱 timestamp 接收新样本，暂停和空信号也更新状态，避免每个展示帧重复摄入同一遥测。
- 网点边缘改为相邻 fragment quad 的差分，保留原版点阵形状与动态阈值；仍有 tone 变化和浮点 / 采样边缘差异，不宣称完全相同。

聚焦检查包括真实 Avalonia 控件的 dispatcher 主循环连续出帧（并非仅检查 timer.IsEnabled）、隐藏 / 恢复及设置；Release 构建通过。最新版 macOS arm64 AOT 已发布并运行网点 / 涟光的切换序列。其余高级参数、自定义 MV 与跨平台设备限制沿用上节说明。

重现确定性原生序列：

```sh
dotnet run --project tools/Echo.BackgroundPreview -c Release -- misc/background-motion --sequence
dotnet run --project tools/Echo.BackgroundPreview -c Release -- misc/background-motion-light --sequence --light
dotnet run --project tools/Echo.BackgroundPreview -c Release -- misc/background-reactive flowingLight geometricAmbience --sequence --reactive
dotnet run --project tools/Echo.BackgroundPreview -c Release -- misc/background-transition coverFlow coverPond flowingLight geometricAmbience --sequence --transitions
```
