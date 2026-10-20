using Avalonia.Data.Converters;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Launch;

namespace PzLauncher.Desktop.Localization;

public static class DisplayNames
{
    public static FuncValueConverter<PzInstallKind, string> InstallKind { get; } = new(kind => Of(kind));

    public static FuncValueConverter<PzInstallSource, string> InstallSource { get; } = new(source => Of(source));

    public static FuncValueConverter<JavaRuntimeSource, string> JavaSource { get; } = new(source => Of(source));

    public static FuncValueConverter<InstalledModSource, string> ModSource { get; } = new(source => Of(source));

    public static FuncValueConverter<ZombieBuddyChannel, string> ZombieBuddyChannelName { get; } = new(channel => Of(channel));

    public static FuncValueConverter<ZombieBuddyStep, string> ZombieBuddyStepName { get; } = new(step => Of(step));

    public static FuncValueConverter<WorkshopItemState, string> WorkshopItemStateName { get; } = new(state => Of(state));

    public static FuncValueConverter<WorkshopItemFailure, string> WorkshopItemFailureName { get; } = new(failure => Of(failure));

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

    public static string Of(ProxyMode mode) => mode switch
    {
        ProxyMode.System => UiText.ProxyMode_System,
        ProxyMode.None => UiText.ProxyMode_None,
        ProxyMode.Manual => UiText.ProxyMode_Manual,
    };

    public static string Of(AfterLaunchAction action) => action switch
    {
        AfterLaunchAction.Keep => UiText.AfterLaunchAction_Keep,
        AfterLaunchAction.Minimize => UiText.AfterLaunchAction_Minimize,
        AfterLaunchAction.MinimizeToTray => UiText.AfterLaunchAction_MinimizeToTray,
        AfterLaunchAction.Close => UiText.AfterLaunchAction_Close,
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

    public static string Of(WorkshopItemState state) => state switch
    {
        WorkshopItemState.UpToDate => UiText.WorkshopItemState_UpToDate,
        WorkshopItemState.NeedsUpdate => UiText.WorkshopItemState_NeedsUpdate,
        WorkshopItemState.NotInstalled => UiText.WorkshopItemState_NotInstalled,
        WorkshopItemState.DisabledLocally => UiText.WorkshopItemState_DisabledLocally,
        WorkshopItemState.NotSubscribed => UiText.WorkshopItemState_NotSubscribed,
    };

    public static string Of(WorkshopItemFailure failure) => failure switch
    {
        WorkshopItemFailure.SubscribeFailed => UiText.WorkshopItemFailure_SubscribeFailed,
        WorkshopItemFailure.UnsubscribeFailed => UiText.WorkshopItemFailure_UnsubscribeFailed,
        WorkshopItemFailure.DownloadNotStarted => UiText.WorkshopItemFailure_DownloadNotStarted,
        WorkshopItemFailure.DownloadFailed => UiText.WorkshopItemFailure_DownloadFailed,
        WorkshopItemFailure.TimedOut => UiText.WorkshopItemFailure_TimedOut,
        WorkshopItemFailure.NotReported => UiText.WorkshopItemFailure_NotReported,
    };
}
