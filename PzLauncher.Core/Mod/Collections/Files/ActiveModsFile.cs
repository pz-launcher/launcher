using System.Text.RegularExpressions;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Collections.Files;

/// <summary>
/// The game's enabled mod list, <c>mods/default.txt</c>. Only its <c>mods { }</c> block is ever rewritten; the
/// <c>VERSION</c> line, <c>maps { }</c> and everything else stay as they are.
/// </summary>
internal sealed partial class ActiveModsFile
{
    private const string Indent = "    ";

    private readonly GameTextFile _file;

    // The block's line range, inclusive; Start < 0 when the file has none.
    private int _start;
    private int _end;

    private ActiveModsFile(GameTextFile file, int start, int end, IReadOnlyList<string> modIds)
    {
        _file = file;
        _start = start;
        _end = end;
        ModIds = modIds;
    }

    /// <summary>In load order; empty for a missing file.</summary>
    public IReadOnlyList<string> ModIds { get; private set; }

    /// <summary><paramref name="newline"/> is for a file the launcher creates; an existing file keeps its own.</summary>
    public static async Task<Result<ActiveModsFile>> ReadAsync(string cache, string newline, CancellationToken ct)
    {
        var read = await GameTextFile.ReadAsync(ModCollectionFiles.ActiveModsFile(cache), newline, ModCollectionErrors.TextFile, ct).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            return read.Error;
        }

        var file = read.Value;
        if (!file.Exists)
        {
            List<string> template = [ModCollectionFiles.ActiveModsVersionLine, "", .. Block([]), "", ModCollectionFiles.MapsBlock, "{", "}"];
            foreach (var line in template)
            {
                file.Add(line);
            }
        }

        return Parse(file);
    }

    public void SetModIds(IReadOnlyList<string> modIds)
    {
        var block = Block(modIds);
        if (_start < 0)
        {
            Insert(block);
        }
        else
        {
            var last = _file.Lines[_end].Terminator;
            _file.Lines.RemoveRange(_start, _end - _start + 1);
            _file.Lines.InsertRange(_start, block.Select((line, i) => new TextLine(line, i == block.Count - 1 ? last : _file.Newline)));
            _end = _start + block.Count - 1;
        }

        ModIds = modIds;
    }

    /// <summary>Keeps the first version of an existing file as <c>default.txt.pzlauncher.orig</c>.</summary>
    public Task<Result> WriteAsync(CancellationToken ct) => _file.WriteAsync(rollingBackup: true, _file.FilePath + ModCollectionFiles.OriginalSuffix, ct);

    private static Result<ActiveModsFile> Parse(GameTextFile file)
    {
        var lines = file.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Content.Trim();
            int open;
            if (BlockOpenLine().IsMatch(trimmed))
            {
                open = i;
            }
            else if (trimmed == ModCollectionFiles.ActiveModsBlock && NextNonBlank(lines, i + 1) is { } brace && lines[brace].Content.Trim() == "{")
            {
                open = brace;
            }
            else
            {
                continue;
            }

            var ids = new List<string>();
            for (var j = open + 1; j < lines.Count; j++)
            {
                var entry = lines[j].Content.Trim();
                if (entry == "}")
                {
                    return new ActiveModsFile(file, i, j, ids);
                }

                if (ModEntry().Match(entry) is { Success: true } match && match.Groups["id"].Value is { Length: > 0 } id)
                {
                    ids.Add(id);
                }
            }

            return ModCollectionErrors.FileMalformed(file.FilePath, "The mods block has no closing brace.");
        }

        return new ActiveModsFile(file, -1, -1, []);
    }

    private static int? NextNonBlank(List<TextLine> lines, int from)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i].Content))
            {
                return i;
            }
        }

        return null;
    }

    // The game's own shape: "mods", "{", "    mod = id,", "}".
    private static List<string> Block(IReadOnlyList<string> modIds) =>
        [ModCollectionFiles.ActiveModsBlock, "{", .. modIds.Select(id => $"{Indent}{ModCollectionFiles.ActiveModKey} = {id},"), "}"];

    // A file without a mods block gets one before its maps block, else at the end.
    private void Insert(List<string> block)
    {
        var maps = _file.Lines.FindIndex(line => line.Content.Trim() == ModCollectionFiles.MapsBlock);
        var at = maps < 0 ? _file.Lines.Count : maps;
        List<string> lines = maps < 0 ? ["", .. block] : [.. block, ""];
        foreach (var line in lines)
        {
            _file.Insert(at++, line);
        }

        _start = maps < 0 ? at - block.Count : maps;
        _end = _start + block.Count - 1;
    }

    [GeneratedRegex($@"^{ModCollectionFiles.ActiveModsBlock}\s*\{{$")]
    private static partial Regex BlockOpenLine();

    [GeneratedRegex($@"^{ModCollectionFiles.ActiveModKey}\s*=\s*(?<id>.*?)\s*,?$")]
    private static partial Regex ModEntry();
}
