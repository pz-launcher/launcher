using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server.Config;

/// <summary>A server's <c>&lt;name&gt;.ini</c>: <see cref="Set"/> rewrites only that key's lines.</summary>
internal sealed class ServerIniFile
{
    private readonly GameTextFile _file;

    private ServerIniFile(GameTextFile file)
    {
        _file = file;
    }

    /// <summary><c>null</c> when the file doesn't exist.</summary>
    public static async Task<Result<ServerIniFile>?> ReadAsync(string path, CancellationToken ct)
    {
        var read = await GameTextFile.ReadAsync(path, "\n", ServerErrors.TextFile, ct).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            return read.Error;
        }

        if (!read.Value.Exists)
        {
            return null;
        }

        return new ServerIniFile(read.Value);
    }

    public static byte[] Create(IReadOnlyList<KeyValuePair<string, string>> entries) =>
        Encoding.UTF8.GetBytes(string.Concat(entries.Select(entry => $"{entry.Key}={entry.Value}\n")));

    /// <summary>The value of the key's last line, as PZ reads it; <c>null</c> when absent.</summary>
    public string? Get(string key)
    {
        string? value = null;
        foreach (var line in _file.Lines)
        {
            if (Parse(line.Content) is { } entry && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                value = entry.Value;
            }
        }

        return value;
    }

    /// <summary>Replaces every line of the key, or appends one in the file's line-ending style.</summary>
    public void Set(string key, string value)
    {
        var line = $"{key}={value}";
        var found = false;
        for (var i = 0; i < _file.Lines.Count; i++)
        {
            if (Parse(_file.Lines[i].Content) is { } entry && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                _file.Lines[i] = _file.Lines[i] with { Content = line };
                found = true;
            }
        }

        if (!found)
        {
            _file.Add(line);
        }
    }

    /// <summary>Writes nothing when unchanged, and keeps no backup next to PZ's files.</summary>
    public Task<Result> WriteAsync(CancellationToken ct) => _file.WriteAsync(rollingBackup: false, originalCopy: null, ct);

    private static KeyValuePair<string, string>? Parse(string line)
    {
        var text = line.Trim();
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return text.StartsWith('#') || equals <= 0
            ? null
            : new KeyValuePair<string, string>(text[..equals].Trim(), text[(equals + 1)..].Trim());
    }
}
