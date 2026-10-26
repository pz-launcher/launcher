using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services.Mods.Library;

/// <param name="LocalCollections">In the user's order.</param>
/// <param name="Modpacks">Each install's in the user's order, keyed by install root as saved (ordinal); an install without modpacks has no entry.</param>
/// <param name="LocalCollectionsComplete">
/// The file and its local collections were read without dropping anything, so a local collection missing here was deleted, not lost.
/// </param>
public sealed record ModLibrary(
    IReadOnlyList<SavedLocalCollection> LocalCollections,
    IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>> Modpacks,
    bool LocalCollectionsComplete)
{
    public static ModLibrary Empty { get; } = new([], new Dictionary<string, IReadOnlyList<SavedModpack>>(), LocalCollectionsComplete: true);
}

/// <summary>The launcher's local collections and modpacks, in their own file so a launcher that predates them never rewrites it.</summary>
public interface IModLibraryFile
{
    /// <summary>Synchronous, as <see cref="Preferences.IPreferenceStore.Load"/>.</summary>
    ModLibrary Load();

    /// <summary>
    /// Never throws for I/O failures; they are logged. Keeps the other saved fields. Saves made while a write runs are written together after it;
    /// <paramref name="ct"/> only stops waiting for that write.
    /// </summary>
    Task SaveLocalCollectionsAsync(IReadOnlyList<SavedLocalCollection> collections, CancellationToken ct = default);

    /// <summary>Every install's modpacks; an install without any has no entry. <inheritdoc cref="SaveLocalCollectionsAsync"/></summary>
    Task SaveModpacksAsync(IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>> modpacks, CancellationToken ct = default);

    /// <summary>Waits for the running and the waiting write; <paramref name="ct"/> only stops the wait.</summary>
    Task FlushAsync(CancellationToken ct = default);
}
