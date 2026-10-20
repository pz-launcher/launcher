using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop;

/// <param name="Items">In the order of Steam's subscription file.</param>
/// <param name="Warnings">A library's install list that could not be read.</param>
public sealed record WorkshopSubscriptions(IReadOnlyList<WorkshopItem> Items, IReadOnlyList<Error> Warnings);
