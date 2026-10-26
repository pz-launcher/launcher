using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Desktop.Services.Preferences.Records;
using static PzLauncher.Desktop.Services.Preferences.PreferenceJson;

namespace PzLauncher.Desktop.Services.Mods.Library;

public sealed partial class JsonModLibraryFile(string filePath, ILogger logger, TimeProvider time) : IModLibraryFile
{
    private static readonly LibraryFile Empty = new(null, null);

    // Guards _file and _queued: saves come from the UI thread, writes run on the thread pool.
    private readonly Lock _lock = new();

    // One write at a time, so the last save is the one left on disk.
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // Every write is the whole file from this, so saving one field never drops another, unknown ones included.
    private LibraryFile? _file;

    // The write that hasn't taken the file yet; saves made until it does are written by it.
    private TaskCompletionSource? _queued;

    // The latest write; it runs after any earlier one.
    private Task _lastWrite = Task.CompletedTask;

    // Set while the file on disk couldn't be read; it is copied aside before the first write replaces it.
    private bool _keepAside;

    public ModLibrary Load()
    {
        LibraryFile file;
        bool readable;
        lock (_lock)
        {
            file = _file = Read(out readable);
        }

        var localCollections = ToLocalCollections(file.LocalCollections, out var localCollectionsValid);
        if (!localCollectionsValid)
        {
            LogInvalidLocalCollections(logger, filePath);
        }

        var modpacks = ToModpacks(file.Modpacks, out var modpacksValid);
        if (!modpacksValid)
        {
            LogInvalidModpacks(logger, filePath);
        }

        return new ModLibrary(localCollections, modpacks, readable && localCollectionsValid);
    }

    public Task SaveLocalCollectionsAsync(IReadOnlyList<SavedLocalCollection> collections, CancellationToken ct = default)
    {
        LocalCollectionFile[] files =
        [
            .. collections.Select(collection =>
                new LocalCollectionFile(collection.Id.ToString("D", CultureInfo.InvariantCulture), collection.Name, ToElement(collection.Items))),
        ];
        var element = JsonSerializer.SerializeToElement(files, ModLibraryJsonContext.Default.LocalCollectionFileArray);
        return SaveAsync(file => file with { LocalCollections = element }, ct);
    }

    public Task SaveModpacksAsync(IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>> modpacks, CancellationToken ct = default)
    {
        var files = modpacks.ToDictionary(entry => entry.Key, entry => entry.Value.Select(ToFile).ToArray(), StringComparer.Ordinal);
        var element = JsonSerializer.SerializeToElement(files, ModLibraryJsonContext.Default.DictionaryStringModpackFileArray);
        return SaveAsync(file => file with { Modpacks = element }, ct);
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return _lastWrite.WaitAsync(ct);
        }
    }

    private static ModpackFile ToFile(SavedModpack modpack)
    {
        var preset = modpack.Preset;
        var collection = modpack.Collection switch
        {
            SavedModpackCollection.Local local => new ModpackCollectionFile(
                null,
                new ModpackLocalCollectionFile(
                    local.Linked?.ToString("D", CultureInfo.InvariantCulture), new ModpackLocalCollectionCopyFile(local.Name, ToElement(local.Items)))),
            SavedModpackCollection.Steam steam => new ModpackCollectionFile(steam.Id.ToString(CultureInfo.InvariantCulture), null),
            _ => throw new ArgumentException($"Unknown modpack collection {modpack.Collection}.", nameof(modpack)),
        };

        return new ModpackFile(
            modpack.Name,
            new ModpackPresetFile(preset.IsLinked ? preset.Name : null, new ModpackPresetCopyFile(preset.Name, [.. preset.ModIds.Select(TextElement)])),
            collection,
            [.. modpack.ExtraItems.Select(id => TextElement(id.ToString(CultureInfo.InvariantCulture)))]);
    }

    // An entry with a bad id, name or item list, or one repeating an id or name read before, is dropped; a bad item is dropped from its entry.
    private static List<SavedLocalCollection> ToLocalCollections(JsonElement? element, out bool valid)
    {
        if (element is not { ValueKind: JsonValueKind.Array } array)
        {
            valid = element is null or { ValueKind: JsonValueKind.Null };
            return [];
        }

        valid = true;
        var result = new List<SavedLocalCollection>();
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && TryDeserialize(entry, ModLibraryJsonContext.Default.LocalCollectionFile) is { Name: { } name, Items: { ValueKind: JsonValueKind.Array } items } read
                && Guid.TryParseExact(read.Id, "D", out var id)
                && id != Guid.Empty
                && ModpackNames.IsValid(name)
                && ids.Add(id)
                && names.Add(name))
            {
                result.Add(new SavedLocalCollection(id, name, ToWorkshopEntries(items, out var itemsValid)));
                valid &= itemsValid;
            }
            else
            {
                valid = false;
            }
        }

        return result;
    }

    // Per install: a bad root or a non-array is dropped. In a list, a modpack with a bad name, preset or collection, or repeating a name read
    // before, is dropped; a bad or repeated id is dropped from its modpack.
    private static Dictionary<string, IReadOnlyList<SavedModpack>> ToModpacks(JsonElement? element, out bool valid)
    {
        if (element is not { ValueKind: JsonValueKind.Object } map
            || TryDeserialize(map, ModLibraryJsonContext.Default.DictionaryStringJsonElement) is not { } entries)
        {
            valid = element is null or { ValueKind: JsonValueKind.Null };
            return new Dictionary<string, IReadOnlyList<SavedModpack>>(StringComparer.Ordinal);
        }

        var entriesValid = true;
        var result = ReadPerInstall(entries, (JsonElement entry, [MaybeNullWhen(false)] out IReadOnlyList<SavedModpack> modpacks) =>
        {
            modpacks = entry.ValueKind == JsonValueKind.Array ? ToModpackList(entry, ref entriesValid) : null;
            return modpacks is not null;
        });
        valid = entriesValid && result.Count == entries.Count;
        return result;
    }

    private static List<SavedModpack> ToModpackList(JsonElement array, ref bool valid)
    {
        var modpacks = new List<SavedModpack>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in array.EnumerateArray())
        {
            if (ToModpack(entry, ref valid) is { } modpack && names.Add(modpack.Name))
            {
                modpacks.Add(modpack);
            }
            else
            {
                valid = false;
            }
        }

        return modpacks;
    }

    private static SavedModpack? ToModpack(JsonElement element, ref bool valid)
    {
        if (element.ValueKind != JsonValueKind.Object
            || TryDeserialize(element, ModLibraryJsonContext.Default.ModpackFile) is not
            { Name: { } name, Preset: { Copy: { Name: { } copyName, ModIds: { } modIdEntries } } preset, Collection: { } collectionFile } file
            || !ModpackNames.IsValid(name)
            || ToCollection(collectionFile, ref valid) is not { } collection)
        {
            return null;
        }

        // A linked copy carries its preset's name.
        var presetName = preset.Linked ?? copyName;
        if (!ModCollectionName.IsValid(presetName))
        {
            return null;
        }

        List<string> modIds =
        [
            .. modIdEntries
                .Where(entry => entry.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetString() ?? "")
                .Where(id => id.Length > 0),
        ];
        valid &= modIds.Count == modIdEntries.Count;
        var extraItems = ReadById<ulong>(file.ExtraItems ?? [], TryReadIdText, id => id, ref valid);
        return new SavedModpack(name, new SavedModpackPreset(presetName, modIds, preset.Linked is not null), collection, extraItems);
    }

    private static SavedModpackCollection? ToCollection(ModpackCollectionFile file, ref bool valid)
    {
        switch (file)
        {
            case { Steam: { } steam, Local: null }:
                return TryReadWorkshopId(steam, out var id) ? new SavedModpackCollection.Steam(id) : null;
            case { Steam: null, Local: { Copy: { Name: { } name, Items: { ValueKind: JsonValueKind.Array } items } } local }
                when ModpackNames.IsValid(name):
                Guid? linked = null;
                if (local.Linked is { } linkedText)
                {
                    if (!Guid.TryParseExact(linkedText, "D", out var guid) || guid == Guid.Empty)
                    {
                        return null;
                    }

                    linked = guid;
                }

                var entries = ToWorkshopEntries(items, out var itemsValid);
                valid &= itemsValid;
                return new SavedModpackCollection.Local(linked, name, entries);
            default:
                return null;
        }
    }

    private static bool TryReadIdText(JsonElement element, out ulong id)
    {
        id = 0;
        return element.ValueKind == JsonValueKind.String && TryReadWorkshopId(element.GetString(), out id);
    }

    private Task SaveAsync(Func<LibraryFile, LibraryFile> update, CancellationToken ct)
    {
        TaskCompletionSource queued;
        lock (_lock)
        {
            _file = update(_file ?? Read(out _));
            if (_queued is { } waiting)
            {
                return waiting.Task.WaitAsync(ct);
            }

            queued = _queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _lastWrite = queued.Task;
        }

        _ = WriteQueuedAsync(queued);
        return queued.Task.WaitAsync(ct);
    }

    private async Task WriteQueuedAsync(TaskCompletionSource queued)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            LibraryFile file;
            lock (_lock)
            {
                file = _file ?? Empty;
                _queued = null;
            }

            if (_keepAside)
            {
                KeepAside();
            }

            await WriteAsync(filePath, file, ModLibraryJsonContext.Default.LibraryFile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            LogSaveFailed(logger, filePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogSaveFailed(logger, filePath, ex);
        }
        finally
        {
            _writeGate.Release();
            queued.SetResult();
        }
    }

    // A failed copy throws, so the write is skipped and the next save tries again.
    private void KeepAside()
    {
        var stamp = time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var copy = Path.Combine(
            Path.GetDirectoryName(filePath) ?? "", $"{Path.GetFileNameWithoutExtension(filePath)}.unreadable-{stamp}{Path.GetExtension(filePath)}");
        try
        {
            File.Copy(filePath, copy);
            LogKeptAside(logger, filePath, copy);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone since the load: nothing left to overwrite.
        }

        _keepAside = false;
    }

    private LibraryFile Read(out bool readable)
    {
        readable = true;
        try
        {
            using var stream = File.OpenRead(filePath);
            if (JsonSerializer.Deserialize(stream, ModLibraryJsonContext.Default.LibraryFile) is { } file)
            {
                return file;
            }

            readable = false;
            LogLoadFailed(logger, filePath, null);
        }
        catch (FileNotFoundException)
        {
            return Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return Empty;
        }
        catch (IOException ex)
        {
            readable = false;
            LogLoadFailed(logger, filePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            readable = false;
            LogLoadFailed(logger, filePath, ex);
        }
        catch (JsonException ex)
        {
            readable = false;
            LogLoadFailed(logger, filePath, ex);
        }

        _keepAside = true;
        return Empty;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod library {Path} could not be read; starting without local collections and modpacks, and keeping a copy of it before the next save")]
    private static partial void LogLoadFailed(ILogger logger, string path, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable mod library {Path} was copied to {Copy}")]
    private static partial void LogKeptAside(ILogger logger, string path, string copy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local collections in {Path} hold entries that aren't valid; ignoring them")]
    private static partial void LogInvalidLocalCollections(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Modpacks in {Path} hold entries that aren't valid; ignoring them")]
    private static partial void LogInvalidModpacks(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod library {Path} could not be saved")]
    private static partial void LogSaveFailed(ILogger logger, string path, Exception exception);
}
