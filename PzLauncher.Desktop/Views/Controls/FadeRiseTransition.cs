using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace PzLauncher.Desktop.Views.Controls;

/// <summary>Hides the old page at once and fades the new one in while it rises into place.</summary>
public sealed class FadeRiseTransition : IPageTransition
{
    public TimeSpan Duration { get; set; }

    /// <summary>Pixels below its final position where the new page starts.</summary>
    public double Offset { get; set; }

    public Easing Easing { get; set; } = new LinearEasing();

    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (from is not null)
        {
            from.IsVisible = false;
        }

        if (to is null)
        {
            return Task.CompletedTask;
        }

        to.IsVisible = true;
        // Without a transform the animator wraps the visual in a five-transform group; a lone TranslateTransform is enough.
        to.RenderTransform ??= new TranslateTransform();

        var animation = new Animation
        {
            Duration = Duration,
            Easing = Easing,
            Children =
            {
                Frame(0d, opacity: 0d, offset: Offset),
                Frame(1d, opacity: 1d, offset: 0d),
            },
        };

        // RunAsync drops the animated values on completion and on cancel, so the page always ends opaque and in place.
        return animation.RunAsync(to, cancellationToken);
    }

    private static KeyFrame Frame(double cue, double opacity, double offset) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Visual.OpacityProperty, opacity),
            new Setter(TranslateTransform.YProperty, offset),
        },
    };
}
