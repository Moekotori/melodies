# 侧栏图标系统

2026-10-02。用户否定前两版，要求更轻、学习 Apple 的视觉。当前第三版为 18 个单色轻线导航图标和 6 个辅助图标，接入侧栏资源。

设计规则：参考 [Apple SF Symbols 的字重与比例原则](https://developer.apple.com/design/human-interface-guidelines/sf-symbols)，自绘 SVG 统一 24×24 viewBox，导航描边 1.35，辅助控件 1.25，圆头与圆角连接；侧栏容器从 25 缩为 23 DIP。改用轻轮廓和宽松内部留白，仅音符头及列表点保留小面积填充。人物、耳机和文件夹减少内部线；齿轮按圆角齿廓绘制。保留主题单色和选中项整体变色。

资源：`src/Echo.Desktop/Resources/Icons/sidebar/` 的 base SVG；accent SVG 保留为空，阻止既有加载器回退到旧 PNG 双色层。旧 PNG 未删除。颜色由 `EchoMuted` 与 `EchoAccentText` 提供，选中行主体随文字颜色变化。辅助图标使用 sidebar scope，不修改其他页面共享 glyph。

所有权：沿用 `EchoIcon` 实例的几何/图片生命周期和脱离视觉树释放；没有新增渲染引擎、后台任务、动画或 I/O 路径。图标以既有 Avalonia 资源打包，不读取外部路径。各导航 ID 和编辑命令保持原入口。

预览：`tools/Echo.IconPreview` 直接链接产品 `EchoIcon.cs` 与同一批 SVG，独立渲染明暗图标总览、23px 侧栏排版研究及并排对照，输出在 `misc/icon-redesign-v3/preview/`。该侧栏研究用于图标尺寸检查，不是完整主窗口的交互截图。单色 SVG 导出和 ZIP 在同一临时目录；`Echo.SidebarPreview` 的总览文案同步更新。

验证：第三版产品绘制器独立编译 0 警告、0 错误，24 个 SVG 明暗原生渲染及实际尺寸检查通过，源码 diff 检查通过；本轮编译/渲染累计约 5 秒。未重跑完整主窗口和完整依赖树，前一轮并行备份/电台构建阻塞仅为历史记录，不能据此判断当前整树状态。未更新正在运行的应用，未做打包或长测。
