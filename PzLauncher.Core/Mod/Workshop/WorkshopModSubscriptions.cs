using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Workshop;

/// <param name="Items">In the order of Steam's subscription file.</param>
/// <param name="Warnings">Files that could only be read in part: a library's install list, the mod collections.</param>
public sealed record WorkshopModSubscriptions(IReadOnlyList<WorkshopModItem> Items, IReadOnlyList<Error> Warnings);
