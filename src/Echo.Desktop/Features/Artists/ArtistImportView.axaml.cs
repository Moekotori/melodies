using Avalonia.Controls;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistImportView : UserControl, IDisposable
{
    private readonly ArtistImportViewModel? model;
    public ArtistImportView() => InitializeComponent();
    public ArtistImportView(ArtistImportViewModel model, bool showHeading = true) : this()
    {
        this.model = model; DataContext = model;
        ImportTitle.IsVisible = showHeading;
        AttachedToVisualTree += (_,_) => _ = model.LoadAsync();
    }
    public void Dispose() => model?.Dispose();
}
