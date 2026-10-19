# 旧版 CUE 源码来源

初始 ScanCueReader.cs、ScanCueParser.cs、ScanCueMessages.cs 来自旧 ECHO，源 Git 3dbf28993cddf9f10ea355b88b0105abe85e5174。保留原命名空间；ProtocolLimitException 只抽取 parser 所需的异常定义。

2026-10-19 从 moekotori/echosteam Git 1906021d4e860e7c3d479617590d2dda3095c6ac 的 core/Echo.Core/Media 与 Protocol 更新解析实现，并复用 CueSyntax、CueAudioSource、CueTextDecoder、FlacBinaryCueSheet、ScanCueValidation 和批次上限异常。参考检出保持只读。

支持单引号/转义、空格文件名、跨文件 TRACK、原始帧 INDEX、混合数据轨边界、常见代码页及 UTF-16/32、FLAC 二进制 CUESHEET；保留根目录约束、唯一无损替代、每输入 4096 曲目与 1 MiB 输出限制。本仓库额外限制 FLAC 元数据区总计 64 MiB；不读取音频正文或图片。发现层接入单轨、扫描所有的有界引用集合、跨目录抑制与故障保护，不改变原生播放事实所有权。

来源项目许可证 AGPL-3.0，见 native/vendor/echo-library-scanner/LICENSE。
