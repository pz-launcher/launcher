using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>The host must also call <c>services.AddLogging()</c>; a fake <see cref="IPlatformEnvironment"/> registered first wins.</summary>
    public static IServiceCollection AddPzLauncherCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformEnvironment, PlatformEnvironment>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<SteamLocator>();
        services.TryAddSingleton<SteamLibraryReader>();
        services.TryAddSingleton<IInstallDetectionService, InstallDetectionService>();
        services.TryAddSingleton<IJavaDiscoveryService, JavaDiscoveryService>();
        services.TryAddSingleton<IGameLaunchService, GameLaunchService>();
        services.TryAddSingleton<IZombieBuddyService, ZombieBuddyService>();

        return services;
    }
}
