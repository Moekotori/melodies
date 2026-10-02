# 旧版 CUE 源码来源

ScanCueReader.cs、ScanCueParser.cs、ScanCueMessages.cs 原样复制 D:/ECHO/core/Echo.Core，源 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174。保留原命名空间；ProtocolLimitException 只抽取 parser 所需的异常定义。

原 parser 的文件范围检查、WAV 到 FLAC/APE/WV 唯一替代、BOM/UTF-8/GB18030 解码、75 fps INDEX、上一段 end、最大 4096 曲目与 1 MiB 输出限制均保持。上层只适配新快照、数据库和播放 SourceRange。

来源项目许可证 AGPL-3.0，见 native/vendor/echo-library-scanner/LICENSE。
