namespace Echo.Contracts.Library;

public enum TrackSort { Title, Artist, Album, TrackNumber, Year, Duration, SampleRate, BitDepth, Codec, Recent, Modified, Created }
public sealed record TrackOrder(TrackSort Field, bool Descending = false);
public enum FileFormat { All, Flac, Wav, Aiff, Alac, Dsd, Mp3, Aac, Ogg, Opus, Other }
public enum AudioRate { All, Hz44100, Hz48000, Hz88200, Hz96000, Hz176400, Hz192000, Hz352800, Hz384000, Dsd, Other }
public enum LibrarySource { All, Files, Cue, Osu, Remote }
public sealed record LibraryFilter(FileFormat Format = FileFormat.All, AudioRate Rate = AudioRate.All,
    int? BitDepth = null, int? Year = null, string? Genre = null, string? Folder = null,
    bool FavoriteOnly = false, bool LosslessOnly = false, LibrarySource Source = LibrarySource.All);

public interface ITrackLibrary
{
    Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken cancellation = default);
}
