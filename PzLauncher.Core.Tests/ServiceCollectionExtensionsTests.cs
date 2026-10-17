using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPzLauncherCore_ResolvesPublicServicesAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPzLauncherCore();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(provider.GetRequiredService<IInstallDetectionService>(), provider.GetRequiredService<IInstallDetectionService>());
        Assert.Same(provider.GetRequiredService<IJavaDiscoveryService>(), provider.GetRequiredService<IJavaDiscoveryService>());
        Assert.Same(provider.GetRequiredService<IGameLaunchService>(), provider.GetRequiredService<IGameLaunchService>());
        Assert.Same(provider.GetRequiredService<IZombieBuddyService>(), provider.GetRequiredService<IZombieBuddyService>());
        Assert.Same(provider.GetRequiredService<IModCollectionService>(), provider.GetRequiredService<IModCollectionService>());
    }

    [Fact]
    public void AddPzLauncherCore_KeepsPreRegisteredPlatformEnvironment()
    {
        var fake = new FakePlatformEnvironment();
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformEnvironment>(fake);
        services.AddPzLauncherCore();
        using var provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IPlatformEnvironment>());
    }
}
