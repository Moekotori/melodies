namespace Echo.Desktop.Features.LibraryDetails;

public sealed record FilterChoice(string Value, string Label, bool Browse = false)
{ public override string ToString() => Label; }
