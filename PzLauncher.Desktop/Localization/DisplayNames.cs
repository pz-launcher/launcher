using Avalonia.Data.Converters;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Localization;

public static class DisplayNames
{
    public static FuncValueConverter<PzInstallKind, string> InstallKind { get; } = new(kind => Of(kind));

    public static FuncValueConverter<PzInstallSource, string> InstallSource { get; } = new(source => Of(source));

    public static FuncValueConverter<JavaRuntimeSource, string> JavaSource { get; } = new(source => Of(source));

    public static FuncValueConverter<InstalledModSource, string> ModSource { get; } = new(source => Of(source));

    public static FuncValueConverter<ZombieBuddyChannel, string> ZombieBuddyChannelName { get; } = new(channel => Of(channel));

    public static FuncValueConverter<ZombieBuddyStep, string> ZombieBuddyStepName { get; } = new(step => Of(step));

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

    public static string Of(GarbageCollector gc) => gc switch
    {
        GarbageCollector.G1 => UiText.GarbageCollector_G1,
        GarbageCollector.Z => UiText.GarbageCollector_Z,
        GarbageCollector.Shenandoah => UiText.GarbageCollector_Shenandoah,
        GarbageCollector.Parallel => UiText.GarbageCollector_Parallel,
        GarbageCollector.Serial => UiText.GarbageCollector_Serial,
    };

    public static string Of(InstalledModSource source) => source switch
    {
        InstalledModSource.SteamWorkshop => UiText.ModSource_SteamWorkshop,
        InstalledModSource.StagedWorkshop => UiText.ModSource_StagedWorkshop,
        InstalledModSource.Local => UiText.ModSource_Local,
    };

    public static string Of(ZombieBuddyChannel channel) => channel switch
    {
        ZombieBuddyChannel.Stable => UiText.ZombieBuddyChannel_Stable,
        ZombieBuddyChannel.Beta => UiText.ZombieBuddyChannel_Beta,
    };

    public static string Of(UpdateSource source) => source switch
    {
        UpdateSource.Auto => UiText.UpdateSource_Auto,
        UpdateSource.GitHub => UiText.UpdateSource_GitHub,
        UpdateSource.Gitee => UiText.UpdateSource_Gitee,
    };

    public static string Of(ZombieBuddyStep step) => step switch
    {
        ZombieBuddyStep.FindItem => UiText.ZombieBuddyStep_FindItem,
        ZombieBuddyStep.CopyNativeLoader => UiText.ZombieBuddyStep_CopyNativeLoader,
        ZombieBuddyStep.CopyAgentJar => UiText.ZombieBuddyStep_CopyAgentJar,
        ZombieBuddyStep.AddAgentArg => UiText.ZombieBuddyStep_AddAgentArg,
        ZombieBuddyStep.DeleteFiles => UiText.ZombieBuddyStep_DeleteFiles,
        ZombieBuddyStep.RemoveAgentArg => UiText.ZombieBuddyStep_RemoveAgentArg,
    };
}
