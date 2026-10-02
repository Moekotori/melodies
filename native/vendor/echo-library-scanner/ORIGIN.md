# 旧版 ECHO 原生扫描器来源

直接复制 D:/ECHO/native/library-scanner，源 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174，日期 2026-10-02。

CMakeLists.txt 与 src 文件保持原样，包含 main.cpp、music_graph 和 metadata_file_tail 等模块。main.cpp 是未改写的既有源文件，按 vendor snapshot 管理；新适配代码继续按职责拆分，不向该大文件追加应用业务。

许可证随 LICENSE 保留，AGPL-3.0。构建产物放入 Git 忽略的 artifacts/，正式分发前仍按源版本生成 notices 和 source 清单。
