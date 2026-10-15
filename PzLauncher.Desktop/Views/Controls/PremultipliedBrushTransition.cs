using System;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PzLauncher.Desktop.Views.Controls;

// BrushTransition blends straight alpha, so fading from Transparent (#00FFFFFF) flashes light over dark backgrounds.
public sealed class PremultipliedBrushTransition : InterpolatingTransitionBase<IBrush?>
{
    public static Color Blend(Color from, Color to, double progress)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        var fromAlpha = from.A / 255d;
        var toAlpha = to.A / 255d;
        var alpha = Lerp(fromAlpha, toAlpha, progress);
        if (alpha <= 0d)
        {
            return Colors.Transparent;
        }

        byte Channel(byte f, byte t) => (byte)Math.Round(Lerp(f * fromAlpha, t * toAlpha, progress) / alpha);

        return Color.FromArgb((byte)Math.Round(alpha * 255d), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    protected override IBrush? Interpolate(double progress, IBrush? from, IBrush? to)
    {
        if (from is not ISolidColorBrush fromBrush || to is not ISolidColorBrush toBrush)
        {
            return progress >= 0.5 ? to : from;
        }

        return new ImmutableSolidColorBrush(Blend(Effective(fromBrush), Effective(toBrush), progress));
    }

    private static Color Effective(ISolidColorBrush brush) =>
        Color.FromArgb((byte)Math.Round(brush.Color.A * brush.Opacity), brush.Color.R, brush.Color.G, brush.Color.B);

    private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);
}
