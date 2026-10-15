using Avalonia.Controls;
using Avalonia.Media;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // Inter has no CJK glyphs; without named CJK fallbacks, bold Chinese renders at regular weight.
        FontFamily = FontFamily.Parse(UiText.Main_FontFamily);
    }
}
