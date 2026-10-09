using Echo.Infrastructure.Radio;

using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
await ResolverSmoke.RunAsync(budget.Token);
await RadioServiceSmoke.RunAsync(budget.Token);
await RadioFeatureSmoke.RunAsync(budget.Token);
if (args.Length > 0) await NativeRadioSmoke.RunAsync(Path.GetFullPath(args[0]), budget.Token);
Console.WriteLine("PASS radio address resolution, cancellation and playback ownership");
