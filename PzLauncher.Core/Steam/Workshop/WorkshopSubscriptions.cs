using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop;

/// <param name="Items">In the order of Steam's subscription file.</param>
/// <param name="Warnings">Files that could only be read in part: an unreadable library install list, the mod collections.</param>
public sealed record WorkshopSubscriptions(IReadOnlyList<WorkshopItem> Items, IReadOnlyList<Error> Warnings);
