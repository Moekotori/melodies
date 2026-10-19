using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private void UpdateHeroLayout()
    {
        if (_model is null || Bounds.Width <= 0) return;
        var top = TopLevel.GetTopLevel(this);
        var viewportWidth = top?.ClientSize.Width ?? Bounds.Width;
        var viewportHeight = top?.ClientSize.Height ?? 1000;
        var compact = viewportWidth >= 981 && viewportHeight <= 900;
        var narrow = viewportWidth <= 980 || Bounds.Width < 720;
        var pageInset = narrow ? 22d : 42d;
        var contentWidth = Math.Max(1, Bounds.Width - pageInset * 2);
        PageContent.Margin = new(pageInset, compact ? 14 : 20, pageInset, 24);
        var medium = !narrow && (viewportWidth <= 1260 || Bounds.Width < coverBudget(viewportWidth, compact) + 360 + (compact ? 200 : 240) + 64);
        var cover = narrow ? Math.Min(280, contentWidth) : Math.Clamp(contentWidth * .25, 220, compact ? 264 : 304);
        var factsWidth = compact ? 200 : 240;
        var beside = !narrow && !medium;
        Hero.ColumnDefinitions[0].Width = new(cover);
        Hero.ColumnDefinitions[2].Width = new(beside ? factsWidth : 0);
        Hero.ColumnSpacing = narrow ? 20 : Math.Clamp(contentWidth * .035, 24, 44);
        Hero.Margin = new(0, compact ? 28 : 34, 0, compact ? 8 : 14);
        PageContent.RowSpacing = compact ? 12 : 18;
        Hero.RowSpacing = beside ? 0 : narrow ? 20 : 18;
        Tabs.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Grid.SetColumn(CoverButton, 0); Grid.SetRow(CoverButton, 0); Grid.SetColumnSpan(CoverButton, narrow ? 3 : 1);
        Grid.SetColumn(Console, narrow ? 0 : 1); Grid.SetRow(Console, narrow ? 1 : 0); Grid.SetColumnSpan(Console, narrow ? 3 : 1);
        Grid.SetColumn(Facts, beside ? 2 : 0); Grid.SetRow(Facts, beside ? 0 : narrow ? 2 : 1); Grid.SetColumnSpan(Facts, beside ? 1 : 3);
        Facts.BorderThickness = beside ? new(1, 0, 0, 0) : new(0, 1, 0, 0);
        Facts.Padding = beside ? new(compact ? 18 : 24, 4, 0, 4) : new(0, 14, 0, 0);
        Facts.IsVisible = true;
        FactsPanel.Orientation = beside ? Orientation.Vertical : Orientation.Horizontal;
        FactsPanel.ItemSpacing = beside ? (compact ? 9 : 18) : 24;
        CoverFrame.Width = cover; CoverFrame.Height = cover;
        Copy.Spacing = compact ? 7 : 14; Console.Spacing = compact ? 12 : 24;
        var button = compact ? 32 : 40;
        PlayButton.Height = PlayButton.MinHeight = button;
        AlbumFavoriteButton.Width = AlbumFavoriteButton.Height = MoreButton.Width = MoreButton.Height = button;
        var units = _model.Album.Title.Trim().EnumerateRunes().Sum(rune => System.Text.Rune.IsWhiteSpace(rune) ? .32 : rune.Value <= 0x24f ? .58 : 1);
        var density = units >= 48 ? 2 : units >= 30 ? 1 : 0;
        var title = TitleSize(viewportWidth, viewportHeight, density);
        AlbumTitle.FontSize = narrow ? Math.Min(title, 36) : Math.Min(title, Math.Clamp(contentWidth * .042, 36, 50));
        AlbumTitle.LineHeight = AlbumTitle.FontSize * 1.25;
        AlbumTitle.FontWeight = (FontWeight)560;
        var artist = compact ? 16 : 20;
        ArtistLink.FontSize = artist; ArtistLabel.FontSize = artist;
        var compactRows = narrow;
        var rowInset = 0d;
        TrackHeader.Margin = new(rowInset, 0);
        TrackSummary.Margin = new(rowInset, 0);
        TrackHeader.ColumnDefinitions[0].Width = new(compactRows ? 38 : _model.HasDiscs ? 58 : 46);
        TrackHeader.ColumnDefinitions[2].Width = new(compactRows ? 0 : 230);
        TrackHeader.ColumnDefinitions[3].Width = new(compactRows ? 56 : 68);
        TrackHeader.ColumnSpacing = compactRows ? 12 : 18; SignalHeader.IsVisible = !compactRows;
        static double coverBudget(double width, bool shortWindow)
        {
            if (width <= 980) return Math.Min(320, width * .78);
            if (shortWindow) return Math.Max(200, Math.Min(280, width * .22));
            if (width <= 1260) return Math.Max(220, Math.Min(320, width * .30));
            return Math.Max(248, Math.Min(352, width * .24));
        }
        static double TitleSize(double width, double height, int density)
        {
            if (width >= 981 && height <= 900) return density switch { 2 => Math.Clamp(width * .026, 28, 42), 1 => Math.Clamp(width * .031, 32, 50), _ => Math.Clamp(width * .038, 38, 60) };
            if (width >= 981) return density switch { 2 => Math.Clamp(width * .029, 32, 48), 1 => Math.Clamp(width * .036, 38, 60), _ => Math.Clamp(width * .045, 46, 76) };
            return density switch { 2 => Math.Clamp(width * .0255, 30, 44), 1 => Math.Clamp(width * .032, 36, 54), _ => Math.Clamp(width * .04, 42, 68) };
        }
    }
}
