using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private void UpdateHeroLayout()
    {
        if (_model is null || Bounds.Width <= 0) return;
        // All dimensions are in the page's logical coordinates, including app zoom.
        // The scroll viewport stays full width; every content section shares one inset and cap.
        var pageInset = Bounds.Width < 800 ? 22d : 42d;
        var contentWidth = Math.Min(PageContent.MaxWidth, Math.Max(1, Bounds.Width - pageInset * 2));
        var narrow = contentWidth < 640;
        var compact = Bounds.Height <= 620;
        var cover = narrow ? Math.Min(224, contentWidth) : Math.Clamp(contentWidth * .25, 200, compact ? 248 : 320);
        var gap = narrow ? 20 : Math.Clamp(contentWidth * .03, 24, 40);
        var factsWidth = 208d;
        var beside = !narrow && contentWidth >= cover + 320 + factsWidth + gap * 2;

        PageContent.Margin = new(pageInset, compact ? 14 : 20, pageInset, 24);
        PageContent.RowSpacing = compact ? 12 : 18;
        Hero.ColumnDefinitions[0].Width = new(cover);
        Hero.ColumnDefinitions[2].Width = new(beside ? factsWidth : 0);
        Hero.ColumnSpacing = gap;
        Hero.Margin = new(0, 34, 0, compact ? 4 : 8);
        Hero.RowSpacing = beside ? 0 : 20;
        Tabs.Spacing = narrow ? 24 : 30;
        Grid.SetColumn(CoverButton, 0); Grid.SetRow(CoverButton, 0); Grid.SetColumnSpan(CoverButton, narrow ? 3 : 1);
        Grid.SetColumn(Console, narrow ? 0 : 1); Grid.SetRow(Console, narrow ? 1 : 0); Grid.SetColumnSpan(Console, narrow ? 3 : 1);
        Grid.SetColumn(Facts, beside ? 2 : 0); Grid.SetRow(Facts, beside ? 0 : narrow ? 2 : 1); Grid.SetColumnSpan(Facts, beside ? 1 : 3);
        Facts.BorderThickness = beside ? new(1, 0, 0, 0) : new(0, 1, 0, 0);
        Facts.Padding = beside ? new(24, 4, 0, 4) : new(0, 14, 0, 0);
        FactsPanel.Orientation = beside ? Orientation.Vertical : Orientation.Horizontal;
        FactsPanel.ItemSpacing = beside ? 16 : 24;
        CoverFrame.Width = cover; CoverFrame.Height = cover;
        Copy.Spacing = compact || narrow ? 10 : 14;
        Console.Spacing = compact || narrow ? 16 : 24;
        Console.VerticalAlignment = narrow ? VerticalAlignment.Top : VerticalAlignment.Center;

        var units = _model.Album.Title.Trim().EnumerateRunes().Sum(rune => System.Text.Rune.IsWhiteSpace(rune) ? .32 : rune.Value <= 0x24f ? .58 : 1);
        AlbumTitle.FontSize = narrow ? 32 : units >= 48 ? 34 : units >= 30 ? 38 : compact ? 40 : 48;
        AlbumTitle.LineHeight = AlbumTitle.FontSize * 1.25;
        AlbumTitle.FontWeight = FontWeight.SemiBold;
        ArtistLink.FontSize = ArtistLabel.FontSize = narrow ? 18 : 20;

        // The header and realized rows use the same available-width breakpoint.
        var compactRows = contentWidth < 720;
        TrackHeader.ColumnDefinitions[0].Width = new(compactRows ? 38 : _model.HasDiscs ? 58 : 46);
        TrackHeader.ColumnDefinitions[2].Width = new(compactRows ? 0 : 230);
        TrackHeader.ColumnDefinitions[3].Width = new(compactRows ? 56 : 68);
        TrackHeader.ColumnSpacing = compactRows ? 12 : 18;
        SignalHeader.IsVisible = !compactRows;
    }
}
