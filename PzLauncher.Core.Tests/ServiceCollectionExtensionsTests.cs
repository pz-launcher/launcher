using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPzLauncherCore_ResolvesInstallDetectionServiceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPzLauncherCore();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var first = provider.GetRequiredService<IInstallDetectionService>();

        Assert.Same(first, provider.GetRequiredService<IInstallDetectionService>());
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
