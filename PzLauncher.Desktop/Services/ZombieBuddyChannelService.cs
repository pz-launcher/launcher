using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;

namespace PzLauncher.Desktop.Services;

public sealed class ZombieBuddyChannelService : IZombieBuddyChannelService
{
    private readonly IPreferenceStore _store;
    private readonly Dictionary<string, ZombieBuddyChannel> _channels;

    public ZombieBuddyChannelService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform)
    {
        _store = store;
        _channels = new Dictionary<string, ZombieBuddyChannel>(StringComparer.FromComparison(platform.PathComparison));
        foreach (var (root, channel) in saved.ZombieBuddyChannels)
        {
            _channels.TryAdd(root, channel);
        }
    }

    public ZombieBuddyChannel Get(string installRoot) => _channels.GetValueOrDefault(installRoot, ZombieBuddyChannel.Stable);

    public void Save(string installRoot, ZombieBuddyChannel channel)
    {
        // Write under the saved spelling of the path, so a differently cased root replaces its entry instead of adding one.
        var key = _channels.Keys.FirstOrDefault(root => _channels.Comparer.Equals(root, installRoot)) ?? installRoot;
        _channels[key] = channel;

        // SaveZombieBuddyChannelAsync never throws for I/O, and a failed save must not undo the change.
        _ = _store.SaveZombieBuddyChannelAsync(key, channel);
    }
}
