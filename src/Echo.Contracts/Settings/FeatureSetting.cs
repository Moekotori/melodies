namespace Echo.Contracts.Settings;

// Preferences only. Account tokens, passwords and runtime playback facts never belong here.
public sealed record FeatureSetting(bool Enabled = false, string Selection = "", double Number = 0);
