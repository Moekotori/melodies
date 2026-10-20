namespace Echo.Contracts.Lyrics;

public sealed class LyricsLookupUnavailableException() : IOException("网络歌词服务暂不可用，请稍后重新加载或导入本地歌词。");
