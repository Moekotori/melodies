namespace Echo.Contracts.Shortcuts;

public enum ShortcutScope { Local, Global }

public sealed record ShortcutBinding(string Action, ShortcutScope Scope, bool Enabled, string? Accelerator);
public sealed record ShortcutProfile(int Version, IReadOnlyList<ShortcutBinding> Bindings);
public sealed record ShortcutRegistration(string Action, string Accelerator, bool Registered, string? Error = null);

// Backend owns OS registrations. No UI, audio, polling, or settings-file ownership.
public interface IGlobalShortcutBackend : IAsyncDisposable
{
    event Action<string>? Triggered;
    Task<IReadOnlyList<ShortcutRegistration>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings, CancellationToken cancellation);
}

public interface IShortcutProfileStore : IAsyncDisposable
{
    Task<ShortcutProfile?> ReadAsync(CancellationToken cancellation);
    void Save(ShortcutProfile profile);
    event Action<string>? Error;
}
