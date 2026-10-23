using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Collections.Files;

/// <summary>
/// The game's <c>Lua/pz_modlist_settings.cfg</c>: an optional <c>!fav!</c> first line, then one <c>name:id1;id2;</c>
/// line per collection. Edits touch only the edited collection's lines.
/// </summary>
internal sealed class ModListSettingsFile
{
    private readonly GameTextFile _file;

    private ModListSettingsFile(GameTextFile file)
    {
        _file = file;
    }

    public static async Task<Result<ModListSettingsFile>> ReadAsync(string cache, CancellationToken ct)
    {
        // The game writes "\n" whatever the OS.
        var file = await GameTextFile.ReadAsync(ModCollectionFiles.SettingsFile(cache), "\n", ct).ConfigureAwait(false);
        return file.IsSuccess ? new ModListSettingsFile(file.Value) : file.Error;
    }

    /// <summary>In the order of each name's first line; a repeated name takes its last line's ids, as the game does.</summary>
    public IReadOnlyList<SharedModCollection> Collections
    {
        get
        {
            var names = new List<string>();
            var ids = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (_, collection) in Parsed())
            {
                if (!ids.ContainsKey(collection.Name))
                {
                    names.Add(collection.Name);
                }

                ids[collection.Name] = collection.ModIds;
            }

            return [.. names.Select(name => new SharedModCollection(name, ids[name]))];
        }
    }

    /// <summary>
    /// Writes <paramref name="name"/>'s line in place of <paramref name="originalName"/>'s first line (or the first line
    /// of <paramref name="name"/> it overwrites), else at the end; their other lines go. <c>false</c> when
    /// <paramref name="originalName"/> has no line.
    /// </summary>
    public bool Save(string? originalName, string name, IReadOnlyList<string> modIds)
    {
        var target = originalName ?? name;
        var parsed = Parsed().ToList();
        var targetLines = parsed.Where(p => p.Collection.Name == target).Select(p => p.Index).ToList();
        if (originalName is not null && targetLines.Count == 0)
        {
            return false;
        }

        var line = ModCollectionShareText.Line(name, modIds);
        var keep = targetLines.Count > 0 ? targetLines[0] : -1;
        if (keep >= 0)
        {
            _file.Lines[keep] = _file.Lines[keep] with { Content = line };
        }

        RemoveLines(parsed.Where(p => p.Index != keep && (p.Collection.Name == target || p.Collection.Name == name)).Select(p => p.Index));
        if (keep < 0)
        {
            _file.Add(line);
        }

        return true;
    }

    /// <summary><c>false</c> when <paramref name="name"/> has no line.</summary>
    public bool Delete(string name)
    {
        var lines = Parsed().Where(p => p.Collection.Name == name).Select(p => p.Index).ToList();
        RemoveLines(lines);
        return lines.Count > 0;
    }

    public Task<Result> WriteAsync(CancellationToken ct) => _file.WriteAsync(keepOriginal: false, ct);

    private IEnumerable<(int Index, SharedModCollection Collection)> Parsed()
    {
        for (var i = 0; i < _file.Lines.Count; i++)
        {
            var content = _file.Lines[i].Content;
            if (i == 0 && content.StartsWith(ModCollectionFiles.FavouritesPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (ModCollectionShareText.ParseLine(content) is { } collection)
            {
                yield return (i, collection);
            }
        }
    }

    private void RemoveLines(IEnumerable<int> indexes)
    {
        foreach (var index in indexes.OrderDescending())
        {
            _file.Lines.RemoveAt(index);
        }
    }
}
