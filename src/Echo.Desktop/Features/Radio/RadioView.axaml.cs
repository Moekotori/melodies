using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Application.Radio;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Radio;

public partial class RadioView : UserControl, IDisposable
{
    private bool disposed;
    public RadioViewModel Model { get; private set; } = null!;
    public IReadOnlyList<string> Codecs { get; } = ["—", "MP3", "AAC", "OGG", "Opus", "FLAC", "HLS", "WAV"];
    private static readonly FilePickerFileType PlaylistType = new("M3U 电台列表") { Patterns = ["*.m3u", "*.m3u8"], MimeTypes = ["audio/x-mpegurl"] };
    public RadioView() { InitializeComponent(); SizeChanged += (_, _) => ArrangePage(); KeyDown += OnKey; }
    public RadioView(ShellViewModel shell) : this()
    {
        Model = new(shell); DataContext = Model;
        Model.PropertyChanged += ModelChanged;
        AttachedToVisualTree += (_, _) => _ = Model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RadioViewModel.EditorOpen))
        {
            if (Model.EditorOpen) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!disposed) StationNameField.Focus(); });
            else AddButton.Focus();
        }
    }
    private void ArrangePage()
    {
        var compact = Bounds.Width < 764;
        PageLayout.Margin = new Thickness(compact ? 24 : 32, 28, compact ? 24 : 32, 28);
        var stackHeader = Bounds.Width < 560;
        PageHeader.RowDefinitions = new(stackHeader ? "Auto,Auto" : "Auto");
        PageHeader.ColumnDefinitions = new(stackHeader ? "*" : "*,Auto");
        Grid.SetColumn(HeaderTools, stackHeader ? 0 : 1); Grid.SetRow(HeaderTools, stackHeader ? 1 : 0);
        HeaderTools.Margin = stackHeader ? new(0, 14, 0, 0) : default;
        ServiceNote.IsVisible = !compact || Bounds.Height >= 650;
        FeatureCard.IsVisible = !compact || Bounds.Height >= 520;
        FeatureGrid.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
        FeatureGrid.ColumnDefinitions = new(compact ? "*" : "1.05*,0.95*");
        Grid.SetColumn(TunerPanel, compact ? 0 : 1); Grid.SetRow(TunerPanel, compact ? 1 : 0);
        TunerPanel.Margin = compact ? new(0, 18, 0, 0) : default;
        // Hide the scale at very short window heights so the list retains a usable viewport.
        TunerPanel.IsVisible = !compact || Bounds.Height >= 690;
        var stackSearch = Bounds.Width < 600;
        LibraryToolbar.RowDefinitions = new(stackSearch ? "Auto,Auto" : "Auto");
        LibraryToolbar.ColumnDefinitions = new(stackSearch ? "*" : "*,210");
        Grid.SetColumn(SearchArea, stackSearch ? 0 : 1); Grid.SetRow(SearchArea, stackSearch ? 1 : 0);
        SearchArea.Margin = stackSearch ? new(0, 8, 0, 0) : default;
        var narrowColumns = Bounds.Width - PageLayout.Margin.Left - PageLayout.Margin.Right < 700;
        ColumnHeaders.ColumnDefinitions = new(narrowColumns ? "36,*,0,0,0,96" : "44,*,128,104,90,108");
        FrequencyHeader.IsVisible = GenreHeader.IsVisible = CodecHeader.IsVisible = !narrowColumns;
    }
    private void OnEnter(object? sender, RoutedEventArgs args) { if (Model.CanEnter) Model.StageOpen = true; }
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && Model.EditorOpen) { Model.CloseEditorCommand.Execute(null); args.Handled = true; }
        else if (args.Key == Key.Escape && Model.ConfirmClear) { Model.CancelClearCommand.Execute(null); args.Handled = true; }
        else if (args.Key == Key.Escape && Model.StageOpen) { Model.StageOpen = false; args.Handled = true; }
        else if (args.Key == Key.Enter && Model.EditorOpen && args.Source is not ComboBox) { Model.SaveEditorCommand.Execute(null); args.Handled = true; }
    }
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || Model.IsBusy) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "导入电台", AllowMultiple = false, FileTypeFilter = [PlaylistType] });
            if (files.Count == 0 || disposed) return;
            await Model.ImportFromAsync(async token =>
            {
                await using var stream = await files[0].OpenReadAsync();
                var buffer = new byte[RadioPlaylist.MaxBytes + 1]; var length = 0;
                while (length < buffer.Length)
                { var read = await stream.ReadAsync(buffer.AsMemory(length), token); if (read == 0) break; length += read; }
                if (length > RadioPlaylist.MaxBytes) throw new IOException("M3U 文件不能超过 1 MiB。");
                return new UTF8Encoding(false, true).GetString(buffer, 0, length).TrimStart('\uFEFF');
            });
        }
        catch (Exception error) { if (!disposed) Model.Message = error.Message; }
    }
    private async void OnExport(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || Model.IsBusy) return;
        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = "导出电台", SuggestedFileName = "ECHO Radio.m3u", DefaultExtension = "m3u", FileTypeChoices = [PlaylistType] });
            if (file is null || disposed) return;
            await Model.ExportToAsync(async (text, token) =>
            {
                await using var stream = await file.OpenWriteAsync(); await Task.Run(() => stream.SetLength(0), token);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token); await stream.FlushAsync(token);
            });
        }
        catch (Exception error) { if (!disposed) Model.Message = error.Message; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (Model is not null) { Model.PropertyChanged -= ModelChanged; Model.Dispose(); }
    }
}
