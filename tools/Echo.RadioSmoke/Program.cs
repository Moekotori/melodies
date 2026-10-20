using Echo.Infrastructure.Radio;

using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(args.Contains("--network") ? 55 : 35));
if (args.Contains("--network")) { await NetworkDirectorySmoke.RunAsync(budget.Token); return; }
await ResolverSmoke.RunAsync(budget.Token);
await RadioServiceSmoke.RunAsync(budget.Token);
await RadioFeatureSmoke.RunAsync(budget.Token);
await RadioDirectorySmoke.RunAsync(budget.Token);
if (args.Length > 0) await NativeRadioSmoke.RunAsync(Path.GetFullPath(args[0]), budget.Token, !args.Contains("--local"));
Console.WriteLine("PASS radio address resolution, cancellation and playback ownership");
