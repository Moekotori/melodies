using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.History;

public sealed partial class HistoryRow : ObservableObject
{
    public HistoryRecord Record { get; }
    public HistoryDay? Day { get; }
    public bool IsDayStart => Day is not null;
    public string DayText { get; }
    public string DaySummary { get; }
    public string DayNumber { get; }
    public string DayCaption { get; }
    public string ArtistLine { get; }
    public string ArtworkPath => Record.Track.Source?.CoverPath ?? Record.Track.Path;
    public double ProgressPercent => Record.Track.DurationSeconds > 0 ? Math.Clamp(Record.PlayedSeconds / Record.Track.DurationSeconds * 100, 0, 100) : 0;
    public string StartedText { get; }
    public string EndedText { get; }
    public string Time { get; }
    public string Progress { get; }
    public string State { get; }
    public string Source { get; }
    public string Details { get; }
    [ObservableProperty] private bool expanded;
    [ObservableProperty] private bool isSelected;
    public HistoryRow(HistoryRecord record, HistoryDay? day, LocalizationService text)
    {
        Record = record; Day = day;
        var started = DateTimeOffset.FromUnixTimeSeconds(record.StartedUnix).ToLocalTime();
        DayText = started.ToString("dd  MMMM  dddd", text.Current.Culture);
        DayNumber = started.ToString("dd", text.Current.Culture);
        DayCaption = started.ToString("MMMM · dddd", text.Current.Culture);
        DaySummary = day is null ? "" : text.Format("history.daySummary", day.Plays, day.Tracks, HistoryText.Clock(day.Seconds));
        Time = started.ToString("HH:mm", text.Current.Culture);
        Progress = HistoryText.Clock(record.PlayedSeconds) + (record.Track.DurationSeconds > 0 ? " / " + HistoryText.Clock(record.Track.DurationSeconds) : "");
        State = text[record.EndedUnix is null ? "history.pending" : record.Counted ? "history.counted" : "history.uncounted"];
        Source = record.Source.Length == 0 ? text["history.local"] : record.Source;
        ArtistLine = (record.Track.Artist.Length == 0 ? text["history.unknown"] : record.Track.Artist) + " · " + Source;
        var ended = record.EndedUnix is { } end ? DateTimeOffset.FromUnixTimeSeconds(end).ToLocalTime().ToString("g", text.Current.Culture) : text["history.pending"];
        StartedText = started.ToString("g", text.Current.Culture); EndedText = ended;
        Details = text.Format("history.detail", started.ToString("g", text.Current.Culture), ended, Source, Progress) + "\n" + record.Track.Path;
    }
}

internal static class HistoryText
{
    public static string Clock(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1 ? $"{(long)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}" : $"{(long)duration.TotalMinutes}:{duration.Seconds:00}";
    }
}
