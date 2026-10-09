using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Connect;

public sealed record ConnectFact(string Label, string Value);
public sealed record ConnectStep(string Text);

public sealed partial class ConnectServiceCard : ObservableObject
{
    public required string Kicker { get; init; }
    public required string Title { get; init; }
    public required string GuideTitle { get; init; }
    public required IReadOnlyList<ConnectStep> Steps { get; init; }
    public required IRelayCommand PrimaryCommand { get; init; }
    public ObservableCollection<ConnectFact> Facts { get; } = [];
    [ObservableProperty] private string state = "未开启";
    [ObservableProperty] private bool isLive;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private string primaryLabel = "";

    public void SetFacts(params ConnectFact[] facts)
    {
        Facts.Clear();
        foreach (var fact in facts) Facts.Add(fact);
    }
}
