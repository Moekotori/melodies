using Avalonia.Controls;
using Avalonia.Input;

namespace Echo.Desktop.Theming;

// Horizontal option groups keep ordinary ListBox selection and explicit keyboard navigation.
public sealed class ThemeChoiceList : ListBox
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Items.Count > 0 && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            SelectedIndex = e.Key switch
            {
                Key.Home => 0, Key.End => Items.Count - 1,
                Key.Left => Math.Max(0, SelectedIndex - 1),
                _ => Math.Min(Items.Count - 1, SelectedIndex + 1)
            };
            ContainerFromIndex(SelectedIndex)?.Focus(); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }
}
