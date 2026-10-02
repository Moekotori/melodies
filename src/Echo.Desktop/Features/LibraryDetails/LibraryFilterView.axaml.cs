using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.LibraryDetails;
public partial class LibraryFilterView : UserControl
{
    private LibraryFilterViewModel? observed;
    private bool syncing, foldersLoaded, genresLoaded;
    private CancellationTokenSource? choicesRequest;
    private IReadOnlyList<FilterChoice> folders = [], genres = [];
    public LibraryFilterView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { Attach(); RefreshChoices(); if (observed is { DepthIndex: > 0 } or { SourceIndex: > 0 }) ShowAdvancedOptions(); };
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); Attach(); };
    }
    private void Attach()
    {
        if (DataContext is not LibraryFilterViewModel model || ReferenceEquals(observed, model)) return;
        observed = model; model.PropertyChanged += OnInputChanged; RefreshChoices();
    }
    private void Detach()
    {
        choicesRequest?.Cancel(); choicesRequest = null;
        if (observed is not null) observed.PropertyChanged -= OnInputChanged;
        observed = null; folders = []; genres = []; foldersLoaded = genresLoaded = false;
    }
    public void ShowAdvancedOptions() => MoreFilters.IsExpanded = true;
    private void OnMoreFilters(object? sender, RoutedEventArgs e) => ShowAdvancedOptions();
    private string Label(string key) => this.TryFindResource(key, out var value) ? value?.ToString() ?? key : key;
    private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(LibraryFilterViewModel.Year) or nameof(LibraryFilterViewModel.Genre) or nameof(LibraryFilterViewModel.Folder)) RefreshChoices(); }
    private void RefreshChoices()
    {
        if (observed is not { } model) return;
        syncing = true;
        try
        {
            var years = Enumerable.Range(1920, Math.Clamp(DateTime.Now.Year + 2 - 1920, 1, 181)).Reverse()
                .Select(year => new FilterChoice(year.ToString(System.Globalization.CultureInfo.InvariantCulture), year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Fill(YearChoice, [new("", Label("query.anyYear")), .. years], model.Year);
            Fill(GenreChoice, [new("", Label("query.anyGenre")), .. genres], model.Genre);
            Fill(FolderChoice, [new("", Label("query.allFolders")), .. folders, new("", Label("query.browseFolder"), true)], model.Folder);
        }
        finally { syncing = false; }
    }
    private static void Fill(ComboBox combo, IReadOnlyList<FilterChoice> options, string value)
    {
        var selected = options.FirstOrDefault(item => !item.Browse && item.Value == value);
        if (selected is null) { selected = new(value, value); options = [.. options, selected]; }
        var wasOpen = combo.IsDropDownOpen;
        combo.ItemsSource = options; combo.SelectedItem = selected;
        if (wasOpen) combo.IsDropDownOpen = true;
    }
    private void OnYearSelected(object? sender, SelectionChangedEventArgs e)
    { if (!syncing && observed is { } model && YearChoice.SelectedItem is FilterChoice choice) model.Year = choice.Value; }
    private void OnGenreSelected(object? sender, SelectionChangedEventArgs e)
    { if (!syncing && observed is { } model && GenreChoice.SelectedItem is FilterChoice choice) model.Genre = choice.Value; }
    private async void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (syncing || observed is not { } model || FolderChoice.SelectedItem is not FilterChoice choice) return;
        if (!choice.Browse) { model.Folder = choice.Value; return; }
        FolderChoice.IsDropDownOpen = false;
        await ChooseFolderAsync(model); if (ReferenceEquals(observed, model)) RefreshChoices();
    }
    private async void OnGenreOpened(object? sender, EventArgs e) => await LoadChoicesAsync(false);
    private async void OnFolderOpened(object? sender, EventArgs e) => await LoadChoicesAsync(true);
    private async Task LoadChoicesAsync(bool folder)
    {
        if (observed is not { } model || (folder ? foldersLoaded || model.Folders is null : genresLoaded || model.Genres is null)) return;
        choicesRequest?.Cancel(); using var request = new CancellationTokenSource(); choicesRequest = request;
        try
        {
            if (folder)
            {
                var page = await model.Folders!.GetFoldersAsync(new(Limit: 128), request.Token);
                if (request.IsCancellationRequested || !ReferenceEquals(observed, model)) return;
                folders = page.Items.Select(item => new FilterChoice(item.Path, item.Name)).ToArray(); foldersLoaded = true;
            }
            else
            {
                var page = await model.Genres!.GetGenresAsync(new(Limit: 128, Sort: Echo.Contracts.Library.GenreSort.Name, Descending: false), request.Token);
                if (request.IsCancellationRequested || !ReferenceEquals(observed, model)) return;
                genres = page.Items.Select(item => new FilterChoice(item.Name, item.Name)).ToArray(); genresLoaded = true;
            }
            RefreshChoices();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Existing selection and manual advanced fields remain usable. */ }
        finally { if (ReferenceEquals(choicesRequest, request)) choicesRequest = null; }
    }
    private async Task ChooseFolderAsync(LibraryFilterViewModel model)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var selected = await top.StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false });
        if (ReferenceEquals(DataContext, model) && selected.Count > 0 && selected[0].TryGetLocalPath() is { } path)
            model.Folder = path;
    }
}
