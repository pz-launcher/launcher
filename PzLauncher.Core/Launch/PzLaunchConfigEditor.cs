using System.Text.Json;
using System.Text.Json.Nodes;
using PzLauncher.Core.Common;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Launch;

/// <summary>Edits only the top-level <c>vmArgs</c> of <c>ProjectZomboid64.json</c>; every other value survives, comments and spacing don't.</summary>
internal sealed class PzLaunchConfigEditor
{
    private const string VmArgsKey = "vmArgs";

    // Same leniency as PzLaunchConfigJsonContext; a duplicate key would otherwise lose one of its values on write.
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    private readonly JsonObject _root;
    private readonly string _vmArgsKey;

    private PzLaunchConfigEditor(string path, JsonObject root, string vmArgsKey, IReadOnlyList<string> vmArgs)
    {
        FilePath = path;
        _root = root;
        _vmArgsKey = vmArgsKey;
        VmArgs = vmArgs;
    }

    public string FilePath { get; }

    /// <summary>Empty when the json has no <c>vmArgs</c>.</summary>
    public IReadOnlyList<string> VmArgs { get; private set; }

    public static async Task<Result<PzLaunchConfigEditor>> LoadAsync(string path, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return LaunchErrors.ConfigMissing(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LaunchErrors.ConfigMalformed(path, ex.Message);
        }

        return Parse(json, path);
    }

    public static Result<PzLaunchConfigEditor> Parse(string json, string path)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: ReadOptions);
        }
        catch (JsonException ex)
        {
            return LaunchErrors.ConfigMalformed(path, ex.Message);
        }

        if (node is not JsonObject root)
        {
            return LaunchErrors.ConfigMalformed(path, "The file holds no JSON object.");
        }

        // The launch reads vmArgs case-insensitively, so the edit must hit that same key.
        var keys = root.Select(property => property.Key)
            .Where(key => string.Equals(key, VmArgsKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (keys.Count > 1)
        {
            return LaunchErrors.ConfigMalformed(path, "vmArgs appears more than once.");
        }

        if (keys.Count == 0)
        {
            return new PzLaunchConfigEditor(path, root, VmArgsKey, []);
        }

        if (root[keys[0]] is not JsonArray array
            || array.Any(item => item is not JsonValue value || value.GetValueKind() != JsonValueKind.String))
        {
            return LaunchErrors.ConfigMalformed(path, "vmArgs is not an array of strings.");
        }

        return new PzLaunchConfigEditor(path, root, keys[0], [.. array.Select(item => item!.GetValue<string>())]);
    }

    /// <summary>Writes nothing when <paramref name="vmArgs"/> equals <see cref="VmArgs"/>.</summary>
    public async Task<Result> SaveVmArgsAsync(IReadOnlyList<string> vmArgs, CancellationToken ct)
    {
        if (vmArgs.SequenceEqual(VmArgs, StringComparer.Ordinal))
        {
            return Result.Ok();
        }

        _root[_vmArgsKey] = new JsonArray([.. vmArgs.Select(arg => (JsonNode)JsonValue.Create(arg))]);
        try
        {
            await AtomicJsonFile.WriteAsync(FilePath, _root, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ZombieBuddyErrors.ConfigWriteFailed(FilePath, ex.Message);
        }

        VmArgs = vmArgs;
        return Result.Ok();
    }
}
