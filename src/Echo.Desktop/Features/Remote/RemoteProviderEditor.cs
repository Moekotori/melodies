using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Remote;

namespace Echo.Desktop.Features.Remote;

public sealed record RemoteProviderChoice(string Id, string Label, string Description, string Icon, bool Beta = false);
public sealed record RemoteAuthChoice(string Id, string Label);

public sealed partial class RemoteViewModel
{
    [ObservableProperty] private bool providerPickerOpen;
    [ObservableProperty] private string draftProvider = "webdav";
    [ObservableProperty] private RemoteAuthChoice? draftAuth;
    [ObservableProperty] private string draftUserId = "";
    [ObservableProperty] private string draftRootPath = "/";
    [ObservableProperty] private string draftClientId = "";
    private OneDriveTokens? draftAuthorization;
    [ObservableProperty] private bool draftSyncIndex = true;
    public bool CanSync => SelectedSource?.SyncMode == "index" && !IsBusy;
    public IReadOnlyList<RemoteProviderChoice> ProviderChoices { get; private set; } = [];
    public IReadOnlyList<RemoteAuthChoice> AuthChoices { get; private set; } = [];
    public bool IsMounted => RemoteProviders.IsMounted(DraftProvider);
    public bool IsOneDrive => DraftProvider == "onedrive";
    public bool ShowAuthMode => DraftProvider == "subsonic" || RemoteProviders.IsMediaServer(DraftProvider);
    public bool ShowUsername => !IsMounted && !IsOneDrive && DraftAuth?.Id == "password";
    public bool ShowPassword => !IsMounted && !IsOneDrive;
    public bool ShowUserId => RemoteProviders.IsMediaServer(DraftProvider) && DraftAuth?.Id == "token";
    public int UsernameColumnSpan => ShowUsername ? 1 : 2;
    public string EditorTitle => shell.Text.Format("remote.connectProvider", ProviderChoices.FirstOrDefault(x => x.Id == DraftProvider)?.Label ?? DraftProvider);
    public string AddressLabel => shell.Text[IsMounted ? "remote.mountFolder" : DraftProvider == "webdav" ? "remote.url" : "remote.serverUrl"];
    public string AddressHint => shell.Text[IsMounted ? "remote.mountHint" : DraftProvider == "webdav" ? "remote.urlHint" : "remote.serverHint"];
    public string PasswordLabel => shell.Text[DraftAuth?.Id == "password" ? "remote.password" : "remote.apiKey"];
    public string AccountStatus => shell.Text[draftAuthorization is null ? "remote.accountNotConnected" : "remote.accountConnected"];
    private void InitializeProviders() { DraftClientId = shell.OneDriveClientId; RefreshProviderLanguage(); }
    private void RefreshProviderLanguage()
    {
        ProviderChoices = RemoteProviders.All.Select(id => new RemoteProviderChoice(id, shell.Text["remote.provider." + id],
            shell.Text["remote.providerHint." + id], id is "smb" or "sshfs" ? "folder" : RemoteProviders.IsMediaServer(id) ? "album" : id == "webdav" ? "disc" : "music", id == "onedrive")).ToArray();
        OnPropertyChanged(nameof(ProviderChoices)); RefreshAuthChoices(); RefreshProviderState();
    }
    private void RefreshAuthChoices()
    {
        var selected = DraftAuth?.Id ?? "password";
        var other = DraftProvider == "subsonic" ? "apiKey" : "token";
        AuthChoices = [new("password", shell.Text["remote.authPassword"]), new(other, shell.Text["remote.authKey"])];
        DraftAuth = AuthChoices.FirstOrDefault(x => x.Id == selected) ?? AuthChoices[0];
        OnPropertyChanged(nameof(AuthChoices));
    }
    partial void OnDraftProviderChanged(string value) { RefreshAuthChoices(); RefreshProviderState(); }
    partial void OnDraftAuthChanged(RemoteAuthChoice? value) => RefreshProviderState();
    partial void OnDraftClientIdChanged(string value) { draftAuthorization = null; OnPropertyChanged(nameof(AccountStatus)); }
    private void RefreshProviderState()
    {
        foreach (var property in new[] { nameof(IsMounted), nameof(IsOneDrive), nameof(ShowAuthMode), nameof(ShowUsername), nameof(ShowPassword),
            nameof(ShowUserId), nameof(UsernameColumnSpan), nameof(EditorTitle), nameof(AddressLabel), nameof(AddressHint), nameof(PasswordLabel), nameof(AccountStatus) }) OnPropertyChanged(property);
    }
    [RelayCommand] private void ChooseProvider(string? id)
    {
        if (IsBusy || id is null || !RemoteProviders.All.Contains(id)) return;
        if (EditorOpen && !IsEditing && id == DraftProvider) { CloseEditor(); return; }
        editingId = null; DraftProvider = id; DraftAuth = AuthChoices[0]; DraftName = ""; DraftUrl = ""; DraftUser = ""; DraftPassword = "";
        DraftUserId = ""; DraftRootPath = "/"; DraftSyncIndex = id != "onedrive"; DraftClientId = shell.OneDriveClientId; draftAuthorization = null;
        ProviderPickerOpen = false; EditorError = ""; RefreshProviderState(); EditorOpen = true;
        RefreshConnectionState();
    }
    [RelayCommand] private void CloseProviderPicker() => ProviderPickerOpen = false;
    private void RestoreProvider(RemoteConnection source)
    {
        DraftProvider = source.Provider; DraftAuth = AuthChoices.FirstOrDefault(x => x.Id == source.AuthMode) ?? AuthChoices[0];
        DraftUserId = source.UserId; DraftRootPath = source.RootPath; DraftSyncIndex = source.SyncMode == "index"; DraftClientId = source.OneDrive?.ClientId ?? shell.OneDriveClientId;
        draftAuthorization = source.OneDrive; RefreshProviderState();
    }
    private RemoteConnection DraftConnection(string id) => new(id, DraftName,
        IsOneDrive ? "https://graph.microsoft.com/v1.0/" : DraftUrl, IsMounted || IsOneDrive ? "" : DraftUser,
        IsMounted || IsOneDrive ? "" : DraftPassword, DraftProvider, DraftAuth?.Id ?? "password", DraftUserId, DraftRootPath, draftAuthorization,
        DraftSyncIndex ? "index" : "browse");
    public Task AuthorizeOneDriveAsync(Func<Uri, CancellationToken, Task> openBrowser) => RunAsync(async token =>
    {
        if (!IsOneDrive || shell.RemoteAuthorization is null) throw new IOException("OneDrive 登录服务尚未装配。");
        var clientId = DraftClientId.Trim();
        var result = await Task.Run(() => shell.RemoteAuthorization.AuthorizeAsync(clientId, openBrowser, token), token);
        token.ThrowIfCancellationRequested(); if (disposed || !EditorOpen) return;
        draftAuthorization = result; RefreshProviderState();
    }, editor: true);
}
