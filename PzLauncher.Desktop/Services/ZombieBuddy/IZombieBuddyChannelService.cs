using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod.ZombieBuddy;

namespace PzLauncher.Desktop.Services.ZombieBuddy;

/// <summary>Per-install ZombieBuddy channel choice, looked up with the platform's path comparison and persisted through <see cref="IPreferenceStore"/>.</summary>
public interface IZombieBuddyChannelService
{
    /// <summary><see cref="ZombieBuddyChannel.Stable"/> when none is saved.</summary>
    ZombieBuddyChannel Get(string installRoot);

    /// <summary>Applies at once; the file write is fire-and-forget.</summary>
    void Save(string installRoot, ZombieBuddyChannel channel);
}

public static class ZombieBuddyChannelServiceExtensions
{
    /// <summary>The item of the saved channel when it is subscribed, else whichever one is; <c>null</c> when none is.</summary>
    public static ZombieBuddyItem? EffectiveItem(this IZombieBuddyChannelService channels, string installRoot, IReadOnlyList<ZombieBuddyItem> subscribed)
    {
        var saved = channels.Get(installRoot);
        return subscribed.FirstOrDefault(item => item.Channel == saved) ?? subscribed.FirstOrDefault();
    }
}
