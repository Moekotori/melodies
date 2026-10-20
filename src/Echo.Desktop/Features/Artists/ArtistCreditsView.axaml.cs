using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistCreditsView : UserControl, IDisposable
{
    public ArtistCreditsViewModel Model { get; } = null!;
    private readonly ArtistImportView? import;
    private readonly IArtistImportNotifications? notifications;
    private bool closed;
    public event Action? Back;
    public ArtistCreditsView() => InitializeComponent();
    public ArtistCreditsView(IArtistCreditLibrary library, ICoreTaskControl? tasks) : this()
    {
        Model = new(library); DataContext = Model;
        notifications = library as IArtistImportNotifications;
        if (notifications is not null) notifications.ArtistImportChanged += Changed;
        if (tasks is not null)
        {
            var editor = new ArtistImportViewModel(library,tasks);
            editor.Rebuilt += () => _ = Model.RefreshAsync();
            import = new(editor,false); ImportHost.Content = import;
        }
        AttachedToVisualTree += (_,_) => _ = Model.LoadPeopleAsync();
        DetachedFromVisualTree += (_,_) => Dispose();
    }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    public void FocusSearch() => PeopleSearch.Focus();
    private void Changed(ArtistImportSettings settings) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!closed) _ = Model.RefreshAsync(); });
    public void Dispose() { if (closed) return; closed = true; if (notifications is not null) notifications.ArtistImportChanged -= Changed; Model?.Dispose(); import?.Dispose(); Back = null; }
}
