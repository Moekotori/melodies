using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
if(args.FirstOrDefault()=="artist-roles")
{
    var root=Path.GetFullPath(args[1]); await ArtistRolesCoreSmoke.PrepareAsync(root);
    AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
    ArtistRolesCoreSmoke.Render(root); return;
}

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/artist-detail");
Directory.CreateDirectory(output);
await ArtistGroupingSmoke.RunAsync(Path.Combine(output, "grouping"));
await ArtistGroupingSmoke.CheckCoreAsync(Path.Combine(output, "grouping"));
await ArtistProviderSmoke.RunAsync(Path.Combine(output, "providers"));
await ArtistConcertSmoke.RunAsync(Path.Combine(output, "concerts"));
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
ArtistGroupingSmoke.Render(Path.Combine(output, "grouping"));
ArtistDetailSmoke.Run(output);
await Task.Run(() => ArtistCoreSmoke.RunAsync(output)).ConfigureAwait(false);
