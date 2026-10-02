using Echo.Contracts.Remote;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Remote;

public sealed class RemoteEntryRow(RemoteEntry entry, LocalizationService text)
{
    public RemoteEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public string Title => Entry.Title ?? (Entry.IsAudio ? System.IO.Path.GetFileNameWithoutExtension(Entry.Name) : Entry.Name);
    public string Secondary => string.IsNullOrWhiteSpace(Entry.Artist) ? Entry.Name : Entry.Artist;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsAudio => Entry.IsAudio;
    public string? Modified => Entry.Modified;
    public string Kind => IsDirectory ? text["remote.folder"] : IsAudio ? Entry.Codec ?? System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() : text["remote.file"];
    public string Icon => IsDirectory ? "folder" : IsAudio ? "music" : "file";
    public string SizeLabel => IsDirectory || Entry.SizeBytes is null ? "—" : Entry.SizeBytes >= 1048576
        ? text.Format("remote.sizeMb", Entry.SizeBytes / 1048576d) : text.Format("remote.sizeKb", Entry.SizeBytes / 1024d);
}
