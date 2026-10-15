using Avalonia.Data.Converters;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Localization;

#pragma warning disable CS8524 // No discard arms on purpose: CS8509 makes a new enum member without a display name a build error.
public static class DisplayNames
{
    public static FuncValueConverter<PzInstallKind, string> InstallKind { get; } = new(kind => Of(kind));

    public static FuncValueConverter<PzInstallSource, string> InstallSource { get; } = new(source => Of(source));

    public static string Of(PzInstallKind kind) => kind switch
    {
        PzInstallKind.Game => UiText.InstallKind_Game,
        PzInstallKind.DedicatedServer => UiText.InstallKind_DedicatedServer,
    };

    public static string Of(PzInstallSource source) => source switch
    {
        PzInstallSource.SteamLibrary => UiText.InstallSource_SteamLibrary,
        PzInstallSource.Manual => UiText.InstallSource_Manual,
    };
}
#pragma warning restore CS8524
