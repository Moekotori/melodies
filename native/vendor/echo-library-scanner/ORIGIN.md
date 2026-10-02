# 旧版 ECHO 原生扫描器来源

直接复制 D:/ECHO/native/library-scanner，源 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174，日期 2026-10-02。

初始快照包含 main.cpp、music_graph 和 metadata_file_tail 等模块。2026-10-02 文件格式对齐增加独立 ffmpeg_metadata 模块和 native_metadata_result 契约；main.cpp 仅调整契约引用、元数据回退调用和能力报告，CMake 可选择链接 FFmpeg。原有快速解析及 music_graph 逻辑保留，不向该大文件追加应用业务。

许可证随 LICENSE 保留，AGPL-3.0。构建产物放入 Git 忽略的 artifacts/，正式分发前仍按源版本生成 notices 和 source 清单。
