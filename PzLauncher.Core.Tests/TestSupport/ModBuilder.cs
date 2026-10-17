using System.Text;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Mod copies as the game finds them: <c>&lt;mods folder&gt;/&lt;folder&gt;/&lt;version&gt;/mod.info</c>.</summary>
public sealed class ModBuilder(string cache, string library)
{
    public string Cache { get; } = cache;

    public string Library { get; } = library;

    public static string ModInfo(
        string id, string? name = null, string? require = null, string? loadAfter = null, string? loadBefore = null, string? incompatible = null)
    {
        var text = new StringBuilder($"name={name ?? id}\nid={id}\n");
        Append(text, "require", require);
        Append(text, "loadAfter", loadAfter);
        Append(text, "loadBefore", loadBefore);
        Append(text, "incompatible", incompatible);
        return text.ToString();
    }

    /// <summary>Returns the version folder.</summary>
    public static string Create(string modFolder, string modInfo, string version = "42")
    {
        var folder = Directory.CreateDirectory(Path.Combine(modFolder, version)).FullName;
        File.WriteAllText(Path.Combine(folder, "mod.info"), modInfo);
        return folder;
    }

    public string Staged(string item, string id, string? modInfo = null) =>
        Create(Path.Combine(Cache, "Workshop", item, "Contents", "mods", id), modInfo ?? ModInfo(id));

    public string Steam(string itemId, string id, string? modInfo = null) =>
        Create(Path.Combine(Library, "steamapps", "workshop", "content", "108600", itemId, "mods", id), modInfo ?? ModInfo(id));

    public string Local(string id, string? modInfo = null) =>
        Create(Path.Combine(Cache, "mods", id), modInfo ?? ModInfo(id));

    private static void Append(StringBuilder text, string key, string? value)
    {
        if (value is not null)
        {
            text.Append(key).Append('=').Append(value).Append('\n');
        }
    }
}
