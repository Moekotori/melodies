# 流派迁移视觉 QA

source visual truth: C:/Users/Moe/AppData/Local/Temp/codex-clipboard-566aa24a-ef04-458e-bd9e-a41c74cbd1c7.png
implementation: misc/genres-migration/preview/genres-light.png
additional states: genres-dark.png, genres-narrow.png, genre-detail-dark.png, genre-tracks-dark.png

Viewport: source 2977×1780 screenshot pixels (conversation displays 2048×1225); implementation native Avalonia 1440×900 logical/pixel, 96 DPI, no CSS/browser layer. Reference aspect-preserving comparison at width 1440 corresponds to 861px height. The extra 39px and existing Avalonia shell are expected migration differences. This is layout/feature migration into the current native shell, not reconstruction of old title/player chrome. Test names/counts/covers are isolated fixture data.
State: Chinese/light, local library, default track-count descending wall. Full reference and final render were opened in the same tool-result comparison; additional dark/detail/narrow renders also inspected. Focused surfaces are legible at this size: title/search toolbar, circular masks, name/stat lines, and bottom clipping.

## Findings and fixes

- P2 first render: title missing because navigation.genres resource did not exist. Added explicit Chinese/English keys; final snapshot compiled the Chinese key. First retry used a preview resource override while waiting for the final compilation.
- P2 first render: max-width search field centered in its star column. Named search area now uses measured bounded width and left alignment. Final screenshot starts search at the same content edge as the title.
- Final geometry: original open wall retained, no individual framed cards. 124px circular real artwork, serif/CJK-aware missing-art marks, centered names and per-genre track/album counts. Seven columns at 1440; three at 800 with toolbar in two rows. Additional clipped row is correctly scrollable above the persistent playback bar.

## Required surfaces

- Typography: existing Echo native font/tokens; 30px heading, 12px semibold card name, 11px statistics. Reference hierarchy retained; responsive column count differs with viewport.
- Spacing/layout: original left heading/search, right source/sort/refresh, circular open grid. Search alignment corrected; 22px horizontal gutters and 216px rows reuse current artist-wall density.
- Colors/tokens: current Echo light/dark semantic resources. Existing native muted-text color is somewhat bluer than old screenshot; intentionally shared with other migrated pages.
- Image quality: actual local album image leases, uniform-fill circular clipping and bounded decoder. Fixture covers copied read-only from local SMTC cache; displayed genre associations/counts are not production facts.
- Copy: flow labels/search/empty/error/count/order translated; canonical metadata shown as stored. Cloud switch disabled with capability explanation because remote library port is absent.

## Primary interaction evidence

SQLite: normalization, casing/fullwidth, unclassified, whole slash tag, paging, retag/album maintenance, missing/restoration, revision/cancellation, backup/backfill assertions passed before the first combined run reached UI.
UI: search/empty/latest generation, End key deep paging, hide/restore, genre→album→genre, paged tracks, simulated play/next/enqueue, live language, narrow layout and disposal passed after creating the missing fixture audio paths. Final visual retry passed.
Native app: no browser console. Snapshot XAML build has five existing unrelated loader warnings; no genre errors. Shared source full-build acceptance and live audio remain unverified; details in docs/GENRES_MIGRATION.md.

## Comparison history

1. Initial inspected render: missing title and search alignment recorded as P2.
2. Added resources and left-aligned adaptive search. Compiled snapshot desktop, copied its own assembly into isolated Preview and rerendered all UI states; final reference/final-render comparison confirms both fixes.

## Implementation checklist

Completed: scoped flow, data owner, cancellation, resource lifetime, native render/interaction checks, P2 visual repairs, migration and rollback documentation.
P3: visible shortcut hint and precise old sort-menu styling can follow existing native control-system consolidation. Remote feature and real device acceptance remain separate.

final result: passed


---

# 2026-10-19 歌词原版场景同步 QA

source visual truth: `misc/lyrics-fidelity/final/reference-blossom.png`、`reference-cassette.png`，由只读 Electron `a44881fcc` 原版引擎在内置浏览器绘制。
implementation screenshot: 同目录 `blossom-6000.png`、`cassette-6000.png`，实际 Skia 原生场景绘图。
full-view comparison evidence: `compare-blossom.png`、`compare-cassette.png`，每张图左为原版、右为原生；两幅画面在同一张图中观察。
Viewport / normalization: 双方 1440×900 像素、同尺寸画布，无缩放裁切。浏览器工具条在源图左上角半透明显示，只属比较工具，不进入产品。
State: 同歌曲、同逐词时间、6000ms、暂停、空间动效关闭；Cassette 白天 / VHS 开启、歌词 36px / 翻译 18px。此比较不将封面有无不同的截图混为同一状态。
Focused evidence: 标题 / 进度条、当前四组词的字号 / 时序 / 字重 / 着色、翻译的位置、磁带及城市的整体构图均在全尺寸并排图内检查，不需要另作局部裁切。

Comparison history:
1. 初始 Cassette native fixture 使用 40 / 22px，而源预览为 36 / 18px，且 save / restore 后阴影状态不完整，造成文字大小和光晕偏差。作为 P2 保留，未据初图通过验收。
2. 对齐 fixture 字号，并修正原生 paint 状态恢复和效果缓存；重新绘制，最终并排图中歌词 / 副文本落点、大小与光晕相符。
3. Blossom 发现 CSS #RRGGBBAA 被当作 ARGB、字体度量和背景短语 fallback 偏差；修正 RGBA 解析、真实字形度量和整段覆盖选择。重绘后的主歌词、翻译、HUD、原场景分镜与配色相符。
4. 连续帧检查发现把离屏反馈也记录为 picture 会增长历史图；改回原版有界降采样位图，只记录最终画面。相邻暖帧工作器绘制约 12–16ms（本机该 fixture），没有据此推断真实 GPU / 整体进程性能。

Final findings:
- 本次两套默认场景静态对照未再发现可操作的 P0 / P1 / P2 构图、落点或文字层次差异。
- P3: Skia 与 Canvas 的字形边缘、背景文字轮廓和后期纹理采样仍有细微差异；不能称为逐像素相同。
- 17 样式 / 5 竖屏布局已由实际 Avalonia 控件绘制；隐藏 / 恢复 / 切走时画面释放与有限帧驱动已检查。来源场景点击使用原版命中逻辑，seek 经现有 offset 换算。
- 检查范围为歌词展示。未进行全库、真实音频设备、其他操作系统、所有动效帧的穷举验收；这不表示这些场景已通过验证。
- 这次为原生 Control，浏览器只加载来源代码；没有新网页产品或外部部署。原生短测未报告场景异常；macOS arm64 Native AOT 发布并运行绘图成功。

final result: passed

---

# 2026-10-19 原版歌词背景同步 QA

source visual truth: 只读 echosteam `a44881fcc` 原版背景在内置 Chromium 绘制；截图 `misc/lyrics-background-sync/final/{coverPond,geometricAmbience,coverFlow,bfs,halftone}.png`。
implementation screenshot: 同目录 `backgrounds/*.png` 为全部 16 个原生背景；`background-aot-render/` 为实际 macOS arm64 Native AOT 绘图；`ui/` 为合回主工作区后真实 Avalonia 控件截图。
full-view comparison evidence: `compare-coverPond.png`、`compare-coverFlow.png`、`compare-bfs.png`、`compare-halftone.png`、`compare-geometricAmbience-final.png`；左侧原版、右侧 Skia 原生，均在同一张图内查看。
Viewport / normalization: 两侧均 640×360 像素，完整画布，无缩放裁切；上方 30px 是比较标签。相同 factory-dark 封面、暗色主题、trackKey 与 16.667ms 初始步长。来源顶部的低透明度 select 是比较工具，不进入产品。实际设置界面另以 1440×900 绘制。
Focused comparisons: 背景为连续铺满场景，完整 1:1 图足以检查涟光折射、几何构图、BFS 节点连线 / 数字和网点场；没有需要另做裁切才能读出的前景小控件。实际抽屉、选项原图、切换后的歌词与控制条另用原生 UI 截图检查。

Comparison history:
1. 初图封面 shader 未建立 mip 链，原版 textureLod 的柔化没有被保留，记录为 P2。修复为七级真实 mipmap 与级间采样后重绘，涟光与封面流动的结构和柔化相符。
2. BFS 的 GLSL 动态全局数组和位操作不被 SkSL 接受，初次绘图失败；改为有界标量访问与等价 15-bit 运算。最终 source / native 节点、连线、波前、数字位置相符。
3. 初始浅色主题下的涟光使用深色歌词 / 控制条，记录为 P2。接入原版背景 ink 规则；`ui/background-pond-static.png` 中歌词及控制条恢复浅色，暗色背景可读。
4. 几何图像与封面采样改用相应高质量缩小采样，重新绘制 `compare-geometricAmbience-final.png`；原版形状、落点、填充和朦胧底色一致。

Final findings:
- 本次默认状态的 5 组完整并排对照未再发现 P0 / P1 / P2 的场景构图或缺图问题。
- P3：Skia 与 WebGL / Canvas 在轮廓抗锯齿、细网点清晰度、封面取样的细微色偏上仍存在差异；不是逐像素一致。
- 16 个动态场景均实际绘出，macOS arm64 AOT 的涟光、几何、BFS 实际绘图通过。合并后的原生界面短测通过选择与原版缩略图、图片背景、深浅主题、隐藏释放 / 恢复、减少动效、独立舞台互斥与偏好持久化。
- 原生短测无场景异常；Release 构建 0 错误，7 项已有 XAML runtime-loader 提醒。浏览器仅作只读来源渲染，没有新网页发布。
- 验收范围是默认背景及本次图片设置；自定义 MV / 视频、创意工坊和全部 shader 高级参数尚未迁入。Windows / Linux、真实音频设备、全部动态帧不在本次验证范围。

final result: passed

---

# 2026-10-19 全部内置背景连续动态复核

source visual truth: 只读 echosteam `a44881fcc` 的原版 WebGL / Canvas renderer；工作区内独立 browser fixture 按生产组件的时间增量、取色、pointer 及更新接口驱动原始模块。没有写入参考检出。
implementation screenshots: `misc/lyrics-background-sync/motion/native/`、`native-light/`、`native-reactive/`、`native-transitions/`；由实际 Skia engine 连续执行后输出。
combined comparison inputs: 同目录 `compare-*.png` 每张上原版、下原生；`comparison-dark-{1..4}.png` 与 `comparison-light-{1..4}.png` 为 16 个背景各自的完整时间序列组合，已逐项查看。
normalization: 单个画面 320×180 像素，组合浏览器 1280×438；双方相同封面、trackKey、主题、固定 30fps（241 帧）及 0 / 1 / 4 / 8 秒，1:1 无裁切。4.033s 按下与移动、6.033s 离开。额外切换比较为 4.0 / 4.1 / 4.4 / 5.0 秒，4.033s 更换封面与主题。抽样时间写在组合图内。高频网点另单独打开完整比较图，避免仅凭拼接缩略图判断。

完整范围：封面流动、流光、几何氛围、液光、山脊、网点、等高线、樱吹雪、广度优先搜索、液态、棱镜、雾窗、涟光、镭射、黑胶、霓虹。不是只比第一帧。静态跟随主题 / 跟随封面 / 自定义图片及跟随页面样式沿用前轮相应检查。

Comparison history:
1. 初始 Cover Flow 在多帧比较中出现明显次色偏蓝，记录为 P2。确认原版从完整原图直接以 Canvas 2D low quality 读取 64²，不能先裁剪或套 shader mip。修正后暗 / 浅 / 中间态均重绘，次色和渐变恢复对应；原版次色和色块层次在最终并排图中对应。浏览器截图工具返回有损编码，已转存为 PNG，但不能据此计算或声称逐像素相似度。
2. 检查代码和切换中间帧发现重建引擎会重置动画，固定 cover key 会跳过后续封面上传。改为有界原实例 update / resize / setTheme / setTrackKey / setScene、递增 cover key；修复后四类渲染器的连续主题与切歌对照相符，保留原版混合时间。
3. 网点的解析梯度不能覆盖点阵折角 / wrap，初图颗粒过于均匀，记录为 P2。替换为相邻 fragment quad 的距离差分，重新绘制深浅两套，最终点阵暗部、亮部和动态布纹对应；与原生后端仍有细微差异。
4. 补齐 pointer leave、捕获范围外的 leave 与排除按钮点击；音乐响应使用新遥测时间戳而非每展示帧摄入。相同 32 频段 fixture 的流光 / 几何响应及暂停后衰减对照相符。

Final findings / verification:
- 16 个背景在暗 / 浅主题的序列均实际绘出；未见静态替代、速度 / 相位明显错位或 P0 / P1 / P2 的动态场景差异。每个背景首尾图像均发生对应变化，几何和雾窗的变化较轻，符合原版。
- 切歌与主题的四类中间态、两类音乐响应另有完整 source / native 组合图，已查看；原生控件在 dispatcher 主循环内连续交付多个新帧，隐藏时停止与释放，恢复及选择 / 偏好短测通过。
- Headless 最初仅 RunJobs 不推进平台 timer；短测改为有界 700ms dispatcher 主循环后再判断实际连续帧，没有把 timer.IsEnabled 当作动画运行证据。
- Release 构建通过。macOS arm64 AOT 最新发布并实际运行网点 / 涟光切换序列通过。未重复扩大到其他业务或真实音频设备。
- P3：浮点哈希细粒、tone 的 quad 差分近似、Skia / WebGL 边缘采样与几何细线仍有像素差异。不能宣称逐像素完全相同，也不能由本轮默认场景推断所有高级参数、MV、平台均已验收。

final result: passed
