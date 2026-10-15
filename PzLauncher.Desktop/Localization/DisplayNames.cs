using Avalonia.Data.Converters;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Localization;

public static class DisplayNames
{
    public static FuncValueConverter<PzInstallKind, string> InstallKind { get; } = new(kind => Of(kind));

    public static FuncValueConverter<PzInstallSource, string> InstallSource { get; } = new(source => Of(source));

    public static FuncValueConverter<JavaRuntimeSource, string> JavaSource { get; } = new(source => Of(source));

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

    // Users only care whether they added it; every discovery source shows as "automatic".
    public static string Of(JavaRuntimeSource source) => source switch
    {
        JavaRuntimeSource.JavaHome
            or JavaRuntimeSource.Path
            or JavaRuntimeSource.Registry
            or JavaRuntimeSource.KnownFolder => UiText.JavaSource_Auto,
        JavaRuntimeSource.Manual => UiText.JavaSource_Manual,
    };
}
