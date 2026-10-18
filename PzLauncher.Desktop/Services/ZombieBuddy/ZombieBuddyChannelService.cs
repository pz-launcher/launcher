using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.ZombieBuddy;

public sealed class ZombieBuddyChannelService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform) : IZombieBuddyChannelService
{
    private readonly IPreferenceStore _store = store;
    private readonly InstallKeyedMap<ZombieBuddyChannel> _channels = new InstallKeyedMap<ZombieBuddyChannel>(saved.ZombieBuddyChannels, platform.PathComparison);

    public ZombieBuddyChannel Get(string installRoot) => _channels.GetValueOrDefault(installRoot, ZombieBuddyChannel.Stable);

    public void Save(string installRoot, ZombieBuddyChannel channel)
    {
        var key = _channels.Set(installRoot, channel);

        // SaveZombieBuddyChannelAsync never throws for I/O, and a failed save must not undo the change.
        _ = _store.SaveZombieBuddyChannelAsync(key, channel);
    }
}
