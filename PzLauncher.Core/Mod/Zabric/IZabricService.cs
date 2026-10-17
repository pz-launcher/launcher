using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;

namespace PzLauncher.Core.Mod.Zabric;

public interface IZabricService
{
    /// <summary>Registers the installed Zabric in ZombieBuddy's preload list when the json's agent arg is allow-all.</summary>
    Task<Result<ZombieBuddyPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);
}
