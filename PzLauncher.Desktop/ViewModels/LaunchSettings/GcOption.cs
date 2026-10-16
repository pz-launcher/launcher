using System.Globalization;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.ViewModels.LaunchSettings;

public sealed class GcOption
{
    private readonly GarbageCollector? _jsonCollector;
    private readonly bool _setByViewpoint;

    private GcOption(GarbageCollector? collector, GarbageCollector? jsonCollector, bool setByViewpoint)
    {
        Collector = collector;
        _jsonCollector = jsonCollector;
        _setByViewpoint = setByViewpoint;
    }

    /// <summary>The value saved for this choice; <c>null</c> follows the json.</summary>
    public GarbageCollector? Collector { get; }

    // Read when bound: a language switch rebuilds the view, which binds it again.
    public string Label => (Collector, _jsonCollector) switch
    {
        ({ } chosen, _) => DisplayNames.Of(chosen),
        (null, null) => UiText.LaunchSettings_GcNotSet,
        (null, { } json) => string.Format(
            CultureInfo.CurrentCulture,
            _setByViewpoint ? UiText.LaunchSettings_GcSetByViewpoint : UiText.LaunchSettings_GcFollowJson,
            DisplayNames.Of(json)),
    };

    internal static GcOption FollowJson(GarbageCollector? jsonCollector, bool setByViewpoint) => new(null, jsonCollector, setByViewpoint);

    internal static GcOption Override(GarbageCollector collector) => new(collector, null, false);
}
