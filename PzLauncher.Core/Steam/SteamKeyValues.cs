using System.Globalization;
using System.Text;
using PzLauncher.Core.Common;
using ValveKeyValue;

namespace PzLauncher.Core.Steam;

internal static class SteamKeyValues
{
    private static readonly KVSerializer Serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    // Root is wrapped in a collection so callers look it up by name like any child.
    // No file loader is set, so #include/#base fail instead of touching the file system.
    public static Result<KVObject> Parse(string text)
    {
        KVDocument document;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
            document = Serializer.Deserialize(stream, CreateOptions());
        }
        catch (KeyValueException ex)
        {
            // The only exception ValveKeyValue's KV1 text reader throws for bad input.
            return SteamErrors.VdfMalformed(ex.Message);
        }

        if (document.Name is null)
        {
            return SteamErrors.VdfMalformed("document has no root key");
        }

        return KVObject.ListCollection([new KeyValuePair<string, KVObject>(document.Name, document.Root)]);
    }

    // The reason is the parser's own, so the caller's message doesn't nest VdfMalformed's.
    public static Result<KVObject> ParseBlock(string text, string rootKey, Func<string, Error> unreadable)
    {
        var parsed = Parse(text);
        if (!parsed.IsSuccess)
        {
            return unreadable(parsed.Error.Details.TryGetValue("reason", out var reason) ? reason : parsed.Error.Message);
        }

        var root = parsed.Value.GetChild(rootKey);
        return root is { IsCollection: true } ? root : unreadable($"missing \"{rootKey}\" block");
    }

    public static async Task<Result<string>> ReadTextAsync(string path, Func<string, Error> unreadable, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return unreadable(ex.Message);
        }
    }

    /// <summary><c>null</c> when the file or its folder doesn't exist.</summary>
    public static async Task<Result<string>?> ReadTextIfExistsAsync(string path, Func<string, Error> unreadable, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return unreadable(ex.Message);
        }
    }

    // Steam isn't consistent about key casing (BetaKey vs betakey); last duplicate wins.
    public static KVObject? GetChild(this KVObject node, string key)
    {
        if (!node.IsCollection)
        {
            return null;
        }

        KVObject? found = null;
        foreach (var (childKey, child) in node.Children)
        {
            if (string.Equals(childKey, key, StringComparison.OrdinalIgnoreCase))
            {
                found = child;
            }
        }

        return found;
    }

    public static string? GetString(this KVObject node, string key) => node.GetChild(key)?.AsText();

    // Number-like values lose their formatting ("007" -> "7"); see design.md "Known limitation".
    public static string? AsText(this KVObject node) =>
        node.IsCollection || node.IsArray || node.IsNull ? null : node.ToString(CultureInfo.InvariantCulture);

    // Repeated keys collapse as in GetChild: last value wins, first position kept.
    public static IReadOnlyList<KeyValuePair<string, KVObject>> GetChildren(this KVObject node)
    {
        if (!node.IsCollection)
        {
            return [];
        }

        var children = new List<KeyValuePair<string, KVObject>>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, child) in node.Children)
        {
            if (index.TryGetValue(key, out var i))
            {
                children[i] = new KeyValuePair<string, KVObject>(children[i].Key, child);
            }
            else
            {
                index[key] = children.Count;
                children.Add(new KeyValuePair<string, KVObject>(key, child));
            }
        }

        return children;
    }

    // Steam escapes \\ and \" in strings, but the library defaults to escapes off.
    // New instance per call: the options object is mutable.
    private static KVSerializerOptions CreateOptions() => new() { HasEscapeSequences = true };
}
