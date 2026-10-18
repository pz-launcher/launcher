using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZBSkipOnlineChecks;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Update;

namespace PzLauncher.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>The host must also call <c>services.AddLogging()</c>; a fake <see cref="IPlatformEnvironment"/> registered first wins.</summary>
    /// <param name="appVersion">The host's informational version; <c>null</c> or unparseable makes a development build that never updates itself.</param>
    public static IServiceCollection AddPzLauncherCore(this IServiceCollection services, string? appVersion = null)
    {
        var currentVersion = SemVersion.FromInformationalVersion(appVersion);

        services.TryAddSingleton<IPlatformEnvironment, PlatformEnvironment>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<SteamLocator>();
        services.TryAddSingleton<SteamLibraryReader>();
        services.TryAddSingleton<WorkshopLibraries>();
        services.TryAddSingleton<ISteamLaunchHookService, SteamLaunchHookService>();
        services.TryAddSingleton<IInstallDetectionService, InstallDetectionService>();
        services.TryAddSingleton<IJavaDiscoveryService, JavaDiscoveryService>();
        services.TryAddSingleton<IGameLaunchService, GameLaunchService>();
        services.TryAddSingleton<IZombieBuddyService, ZombieBuddyService>();
        services.TryAddSingleton<IZombieBuddyModsService, ZombieBuddyModsService>();

        // Steps run in this order and a later one may read an earlier one's outcome.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IZombieBuddyLaunchStep, ZabricLaunchStep>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IZombieBuddyLaunchStep, ZBSkipOnlineChecksLaunchStep>());
        services.TryAddSingleton<IModCollectionService, ModCollectionService>();
        services.TryAddSingleton(_ => new SwitchableProxy(HttpClient.DefaultProxy));
        services.TryAddSingleton(provider => LauncherHttpClient.Create(currentVersion, provider.GetRequiredService<SwitchableProxy>()));
        services.TryAddSingleton<INetworkService, NetworkService>();
        services.TryAddSingleton<IUpdateService>(provider => new UpdateService(
            currentVersion,
            provider.GetRequiredService<LauncherHttpClient>(),
            provider.GetRequiredService<IPlatformEnvironment>(),
            provider.GetRequiredService<IProcessRunner>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<UpdateService>>()));

        return services;
    }
}
