using Avalonia.Media;
using PzLauncher.Desktop.Views.Controls;

namespace PzLauncher.Desktop.Tests.Views.Controls;

public class PremultipliedBrushTransitionTests
{
    [Theory]
    [InlineData("#00FFFFFF", "#FF2E2A26", 0.5, "#802E2A26")]
    [InlineData("#FF2E2A26", "#00FFFFFF", 0.5, "#802E2A26")]
    [InlineData("#FF000000", "#FFFFFFFF", 0.5, "#FF808080")]
    [InlineData("#FF000000", "#FFFFFFFF", 0.0, "#FF000000")]
    [InlineData("#FF000000", "#FFFFFFFF", 1.0, "#FFFFFFFF")]
    public void Blend_Progress_InterpolatesPremultiplied(string from, string to, double progress, string expected)
    {
        Assert.Equal(Color.Parse(expected), PremultipliedBrushTransition.Blend(Color.Parse(from), Color.Parse(to), progress));
    }
}
