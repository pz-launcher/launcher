using PzLauncher.Core.Common;

namespace PzLauncher.Core.Zabric;

public interface IZabricService
{
    /// <summary>Registers the installed Zabric in ZombieBuddy's preload list when the json's agent arg is allow-all.</summary>
    Task<Result<ZabricPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);
}
