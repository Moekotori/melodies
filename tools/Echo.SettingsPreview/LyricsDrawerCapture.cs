using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Settings.Drawers;
using Echo.Desktop.Shell;

// Product drawer only: no playback host, scene engines, library scans or production preferences.
internal static class LyricsDrawerCapture
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var window=SettingsReviewApp.CreateWindow();
        var shell=(ShellViewModel)window.DataContext!;
        try
        {
            window.Show();shell.OpenQuickSettingsCommand.Execute("lyrics");Pump();
            var drawer=window.GetVisualDescendants().OfType<LyricsSettingsDrawerView>().Single();
            var tabs=drawer.FindControl<TabControl>("LyricsTabs")!;
            if(!tabs.Classes.Contains("lyrics-tabs"))
                throw new InvalidOperationException("The preview loaded an older Echo.Desktop assembly. Rebuild the desktop before reviewing the lyrics drawer.");
            shell.LyricsAppearance.BackgroundMode="coverPixelTiles";shell.LyricsAppearance.BackgroundFrameRate=144;
            foreach(var dark in new[]{false,true})
            {
                shell.IsDark=dark;
                foreach(var tab in new[]{0,1,2})
                {
                    tabs.SelectedIndex=tab;Pump();
                    Save($"lyrics-{tab}-{(dark?"dark":"light")}");
                }
            }
            shell.IsDark=false;tabs.SelectedIndex=1;Pump();
            var settings=drawer.FindControl<LyricsBackgroundSettingsView>("LyricBackgroundInput")!;
            var input=settings.FindControl<NumericUpDown>("BackgroundFrameRateInput")!;
            input.Value=120;Pump();
            if(shell.LyricsAppearance.BackgroundFrameRate!=120)throw new InvalidOperationException("Drawer FPS binding failed.");
            var picker=settings.FindControl<ComboBox>("BackgroundInput")!;
            picker.IsDropDownOpen=true;Pump();Save("lyrics-background-options");picker.IsDropDownOpen=false;
            window.Width=900;window.Height=640;shell.UiScalePercent=125;Pump();Save("lyrics-style-compact");
            Console.WriteLine("PASS product lyrics drawer: light/dark display/style/timing, preview picker, FPS input and compact layout");
            void Save(string name)
            {
                Pump();using var image=window.CaptureRenderedFrame()??throw new InvalidOperationException("Drawer capture failed.");
                image.Save(Path.Combine(output,name+".png"),PngBitmapEncoderOptions.Default);
            }
        }
        finally{window.RequestExit();shell.Settings.Dispose();shell.ReleaseLocalization();}
    }
    private static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
}
