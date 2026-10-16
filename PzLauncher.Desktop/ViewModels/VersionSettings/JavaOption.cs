using System.Globalization;
using System.Linq;
using PzLauncher.Core.Java;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.ViewModels.VersionSettings;

public enum JavaOptionKind
{
    /// <summary>The install's own <c>jre64</c>.</summary>
    Bundled,
    Runtime,

    /// <summary>A saved choice that is no longer available; listed so the selection stays visible.</summary>
    Missing,
}

public sealed class JavaOption
{
    private JavaOption(JavaOptionKind kind, string? homePath, JavaRuntime? runtime)
    {
        Kind = kind;
        HomePath = homePath;
        Runtime = runtime;
    }

    public JavaOptionKind Kind { get; }

    /// <summary>The value saved for this choice; <c>null</c> for the bundled Java.</summary>
    public string? HomePath { get; }

    /// <summary><c>null</c> when missing, or when the bundled <c>jre64</c> isn't a usable Java.</summary>
    public JavaRuntime? Runtime { get; }

    public bool IsMissing => Kind == JavaOptionKind.Missing;

    public string? PathText => HomePath ?? Runtime?.HomePath;

    // Read when bound: a language switch rebuilds the view, which binds it again.
    public string Label => Kind switch
    {
        JavaOptionKind.Bundled => string.Format(CultureInfo.CurrentCulture, UiText.VersionSettings_JavaBundled, Detail),
        JavaOptionKind.Runtime => Detail,
        JavaOptionKind.Missing => UiText.Java_Missing,
    };

    private string Detail =>
        Runtime is { } runtime && string.Join(" ", new[] { runtime.Vendor, runtime.Version?.Full }.OfType<string>()) is { Length: > 0 } detail
            ? detail
            : UiText.Java_VersionUnknown;

    internal static JavaOption Bundled(JavaRuntime? runtime) => new(JavaOptionKind.Bundled, null, runtime);

    internal static JavaOption Available(JavaRuntime runtime) => new(JavaOptionKind.Runtime, runtime.HomePath, runtime);

    internal static JavaOption Missing(string homePath) => new(JavaOptionKind.Missing, homePath, null);
}
