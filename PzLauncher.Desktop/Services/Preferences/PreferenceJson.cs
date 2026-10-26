using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>Entry reading and file writing shared by the launcher's JSON files.</summary>
internal static class PreferenceJson
{
    public delegate bool EntryReader<T>(JsonElement element, [MaybeNullWhen(false)] out T value);

    public static T? TryDeserialize<T>(JsonElement element, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return element.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsAbsolutePath(string path) =>
        path.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(path);

    // Decimal text, never zero.
    public static bool TryReadWorkshopId(string? text, out ulong id) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;

    public static JsonElement TextElement(string text) => JsonSerializer.SerializeToElement(text, DesktopJsonContext.Default.String);

    public static List<SavedWorkshopEntry> ToWorkshopEntries(JsonElement? element, out bool valid)
    {
        if (element is not { ValueKind: JsonValueKind.Array } array)
        {
            valid = element is null or { ValueKind: JsonValueKind.Null };
            return [];
        }

        valid = true;
        return ReadById<SavedWorkshopEntry>(array.EnumerateArray(), TryReadWorkshopEntry, entry => entry.Id, ref valid);
    }

    public static JsonElement ToElement(IReadOnlyList<SavedWorkshopEntry> entries)
    {
        WorkshopEntryFile[] files = [.. entries.Select(entry => new WorkshopEntryFile(entry.Id.ToString(CultureInfo.InvariantCulture), entry.Title))];
        return JsonSerializer.SerializeToElement(files, DesktopJsonContext.Default.WorkshopEntryFileArray);
    }

    // An entry that can't be read, or repeats an id read before, is dropped and makes the list invalid.
    public static List<T> ReadById<T>(IEnumerable<JsonElement> entries, EntryReader<T> tryRead, Func<T, ulong> idOf, ref bool valid)
    {
        var result = new List<T>();
        var ids = new HashSet<ulong>();
        foreach (var element in entries)
        {
            if (tryRead(element, out var value) && ids.Add(idOf(value)))
            {
                result.Add(value);
            }
            else
            {
                valid = false;
            }
        }

        return result;
    }

    public static Dictionary<string, T> ReadPerInstall<T>(IReadOnlyDictionary<string, JsonElement>? entries, EntryReader<T> tryRead)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (root, element) in entries ?? new Dictionary<string, JsonElement>())
        {
            if (IsAbsolutePath(root) && tryRead(element, out var value))
            {
                result[root] = value;
            }
        }

        return result;
    }

    public static async Task WriteAsync<T>(string filePath, T content, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        // Write then rename, so a crash mid-write never leaves a truncated file.
        var tempPath = filePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, content, typeInfo, ct);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    private static bool TryReadWorkshopEntry(JsonElement element, [MaybeNullWhen(false)] out SavedWorkshopEntry entry)
    {
        entry = element.ValueKind == JsonValueKind.Object
            && TryDeserialize(element, DesktopJsonContext.Default.WorkshopEntryFile) is { } read
            && TryReadWorkshopId(read.Id, out var id)
                ? new SavedWorkshopEntry(id, read.Title is { Length: > 0 } title ? title : null)
                : null;
        return entry is not null;
    }
}
