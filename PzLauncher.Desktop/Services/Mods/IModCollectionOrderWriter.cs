using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>Writes a collection's new mod order after a short pause, so a drag writes once. Used on the UI thread.</summary>
public interface IModCollectionOrderWriter
{
    /// <summary>Replaces this collection's queued order and restarts the pause; another collection's is written at once. While a game from this session runs, the write waits until it exits.</summary>
    void Queue(string installRoot, string extraGameArgs, string name, IReadOnlyList<string> modIds);

    /// <summary>The queued or still unwritten order of this collection, which a reload must not undo.</summary>
    IReadOnlyList<string>? PendingOrder(string installRoot, string name);

    /// <summary>Raised when a write the pause or the game's exit started has finished; a flush's own write is in its result instead.</summary>
    event EventHandler<Result>? Written;

    /// <summary>Writes the queued order now unless a game runs, and completes when every write has finished.</summary>
    Task<Result> FlushAsync(CancellationToken ct = default);
}
