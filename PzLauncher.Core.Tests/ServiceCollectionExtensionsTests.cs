using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update;

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
        Assert.Same(provider.GetRequiredService<IZombieBuddyModsService>(), provider.GetRequiredService<IZombieBuddyModsService>());
        Assert.Same(provider.GetRequiredService<IModCollectionService>(), provider.GetRequiredService<IModCollectionService>());
        Assert.Same(provider.GetRequiredService<IUpdateService>(), provider.GetRequiredService<IUpdateService>());
        Assert.Same(provider.GetRequiredService<INetworkService>(), provider.GetRequiredService<INetworkService>());
    }

    [Theory]
    [InlineData("0.3.0+e4e3af3", "0.3.0", false)]
    [InlineData(null, "0.0.0-dev", true)]
    public void AddPzLauncherCore_PassesHostVersionToUpdates(string? appVersion, string expected, bool isDevelopment)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPzLauncherCore(appVersion);
        using var provider = services.BuildServiceProvider();

        var updates = provider.GetRequiredService<IUpdateService>();

        Assert.Equal(expected, updates.CurrentVersion.ToString());
        Assert.Equal(isDevelopment, updates.IsDevelopmentBuild);
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
