using Avalonia.Controls;

namespace Echo.Desktop.Features.Home;

public partial class HomeWelcomeView : UserControl
{
    public HomeWelcomeView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 640;
            RecordMotif.IsVisible = !narrow;
            WelcomeLayout.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 200);
            WelcomeLayout.ColumnSpacing = narrow ? 0 : 32;
        };
    }
}
