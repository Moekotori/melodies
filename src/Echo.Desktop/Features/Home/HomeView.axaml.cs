using System.ComponentModel;
using Avalonia.Controls;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Home;

public partial class HomeView : UserControl
{
    private ShellViewModel? _viewModel;
    public HomeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _viewModel = DataContext as ShellViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnStateChanged;
            UpdateTabs();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnStateChanged;
            _viewModel = null;
        };
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.RecentTab)) UpdateTabs();
    }
    private void UpdateTabs()
    {
        PlayedTab.Classes.Set("active", _viewModel?.RecentTab != "added");
        AddedTab.Classes.Set("active", _viewModel?.RecentTab == "added");
        if (_viewModel is not null)
            RecentHost.Content = new RecentActivityEmptyView(_viewModel.RecentEmptyTitle, _viewModel.RecentEmptyDescription,
                _viewModel.RecentTab == "played" ? "history" : "library");
    }
}
