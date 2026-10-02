using Avalonia.Controls;
using Avalonia.Layout;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Settings.Runtime;

public static class LibraryReviewDialog
{
    public static async Task ShowAsync(Window owner, ILibraryReview library, bool hidden,
        SettingsDialogs dialogs, Action changed, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var window = new Window { Title = hidden ? "已隐藏曲目" : "重复曲目检查", Width = 760, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var list = new ListBox { SelectionMode = SelectionMode.Multiple };
        var previous = new Button { Content = "上一页" }; var next = new Button { Content = "下一页" };
        var apply = new Button { Content = hidden ? "恢复选中曲目" : "隐藏选中副本" };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { previous, next, apply } };
        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16, Margin = new(24) };
        body.Children.Add(status); Grid.SetRow(list, 1); body.Children.Add(list); Grid.SetRow(toolbar, 2); body.Children.Add(toolbar);
        window.Content = body;
        var offset = 0; var busy = false;
        IReadOnlyList<LibraryReviewTrack> page = [];
        async Task LoadAsync()
        {
            if (busy) return; busy = true; toolbar.IsEnabled = false;
            try
            {
                var result = await library.GetLibraryReviewAsync(hidden, offset, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                page = result.Items;
                list.ItemsSource = page.Select(track => $"{track.Title} · {track.Artist}\n{track.Path}").ToArray();
                previous.IsEnabled = offset > 0; next.IsEnabled = offset + page.Count < result.Total;
                status.Text = hidden ? $"已隐藏 {result.Total:N0} 首 · 本页 {page.Count} 首。选择后可以恢复。"
                    : $"候选副本 {result.Total:N0} 首 · 本页 {page.Count} 首。\n依据标题、艺人与半秒时长分组；不是音频哈希。请核对后选择，原始音乐文件不会删除。";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!lifetime.IsCancellationRequested) status.Text = error.Message; }
            finally { busy = false; toolbar.IsEnabled = true; }
        }
        previous.Click += async (_, _) => { if (!busy) { offset = Math.Max(0, offset - 200); await LoadAsync(); } };
        next.Click += async (_, _) => { if (!busy) { offset += 200; await LoadAsync(); } };
        apply.Click += async (_, _) =>
        {
            if (busy) return;
            var selected = list.Selection.SelectedIndexes.Where(index => index >= 0 && index < page.Count).Select(index => page[index].Id).ToArray();
            if (selected.Length == 0) { status.Text = "请先选择本页要处理的曲目。"; return; }
            busy = true; toolbar.IsEnabled = false;
            try
            {
                await dialogs.ConfirmAsync(hidden ? "恢复曲目" : "隐藏候选副本", $"将处理已选择的 {selected.Length} 首曲目，仅修改新曲库的隐藏状态。原始音乐文件不变。");
                lifetime.Token.ThrowIfCancellationRequested();
                await library.SetTracksHiddenAsync(selected, !hidden, lifetime.Token); changed();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!lifetime.IsCancellationRequested) status.Text = error.Message; }
            finally { busy = false; toolbar.IsEnabled = true; }
            await LoadAsync();
        };
        window.Closed += (_, _) => lifetime.Cancel();
        window.Opened += async (_, _) => await LoadAsync();
        await window.ShowDialog(owner);
    }
}
