using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.LaunchHook;

/// <summary>
/// One app's <c>LaunchOptions</c> in a <c>localconfig.vdf</c>, changed in place: the rest of the file keeps its bytes,
/// because re-serialising KV1 loses number formatting and Steam's own layout.
/// </summary>
internal sealed class LocalConfigEditor
{
    // localconfig.vdf nests about seven levels; the limit only stops hostile input.
    private const int MaxDepth = 64;

    private readonly byte[] _original;
    private readonly bool _hasBom;
    private readonly string _text;
    private readonly AppBlock _app;

    private LocalConfigEditor(string path, byte[] original, bool hasBom, string text, AppBlock app)
    {
        FilePath = path;
        _original = original;
        _hasBom = hasBom;
        _text = text;
        _app = app;
        LaunchOptions = app.Value is { } value ? Unescape(Content(text, value)) : "";
    }

    public string FilePath { get; }

    /// <summary>Empty when the app has no <c>LaunchOptions</c> key.</summary>
    public string LaunchOptions { get; }

    public static async Task<Result<LocalConfigEditor>> ReadAsync(string path, int appId, CancellationToken ct)
    {
        var read = await ReadFile.BytesIfExistsAsync(path, reason => SteamErrors.LocalConfigUnwritable(path, reason), ct).ConfigureAwait(false);
        if (read is not { } result)
        {
            return SteamErrors.AppNotInLocalConfig(path, appId);
        }

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var bytes = result.Value;
        var decoded = Utf8TextFile.Decode(bytes, reason => SteamErrors.LocalConfigMalformed(path, reason));
        if (!decoded.IsSuccess)
        {
            return decoded.Error;
        }

        var (text, hasBom) = decoded.Value;
        var apps = new List<AppBlock>();
        if (Scan(text, SteamClientFiles.AppBlockPath(appId), apps) is { } problem)
        {
            return SteamErrors.LocalConfigMalformed(path, problem);
        }

        return apps switch
        {
            [] => SteamErrors.AppNotInLocalConfig(path, appId),
            [{ Problem: { } appProblem }] => SteamErrors.LocalConfigMalformed(path, appProblem),
            [var app] => new LocalConfigEditor(path, bytes, hasBom, text, app),
            _ => SteamErrors.LocalConfigMalformed(path, $"app {appId} appears {apps.Count} times"),
        };
    }

    /// <summary>Writes nothing when unchanged; the previous file goes to the rolling <c>.pzlauncher.bak</c> first.</summary>
    public async Task<Result> WriteAsync(string launchOptions, CancellationToken ct)
    {
        if (launchOptions == LaunchOptions && (_app.Value is not null || launchOptions.Length == 0))
        {
            return Result.Ok();
        }

        var text = _app.Value is { } value
            ? string.Concat(_text.AsSpan(0, value.Start), Quote(launchOptions), _text.AsSpan(value.End))
            : Insert(launchOptions);
        try
        {
            await AtomicFile.ReplaceWithBackupAsync(FilePath, _original, Utf8TextFile.Encode(text, _hasBom), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SteamErrors.LocalConfigUnwritable(FilePath, ex.Message);
        }

        return Result.Ok();
    }

    // A new key goes on its own line before the block's closing brace, indented like the key before it.
    private string Insert(string launchOptions)
    {
        var entry = $"\"{SteamClientFiles.LaunchOptionsKey}\"\t\t{Quote(launchOptions)}";
        var close = _app.CloseStart;
        var lineStart = _text.LastIndexOf('\n', close - 1) + 1;
        var braceIndent = _text[lineStart..close];
        if (!IsIndent(braceIndent))
        {
            return string.Concat(_text.AsSpan(0, close), entry, " ", _text.AsSpan(close));
        }

        var indent = braceIndent + "\t";
        if (_app.LastKeyStart is { } key)
        {
            var keyLine = _text.LastIndexOf('\n', key - 1) + 1;
            if (IsIndent(_text.AsSpan(keyLine, key - keyLine)))
            {
                indent = _text[keyLine..key];
            }
        }

        return _text[..lineStart] + indent + entry + Newline() + _text[lineStart..];
    }

    private string Newline()
    {
        var first = _text.IndexOf('\n');
        return first > 0 && _text[first - 1] == '\r' ? "\r\n" : "\n";
    }

    private static bool IsIndent(ReadOnlySpan<char> text) => !text.ContainsAnyExcept(' ', '\t');

    private static string Quote(string value)
    {
        var escaped = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => escaped.Append(@"\\"),
                '"' => escaped.Append("\\\""),
                '\n' => escaped.Append(@"\n"),
                '\t' => escaped.Append(@"\t"),
                _ => escaped.Append(c),
            };
        }

        return escaped.Append('"').ToString();
    }

    private static string Unescape(ReadOnlySpan<char> value)
    {
        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                text.Append(value[i]);
                continue;
            }

            i++;
            _ = value[i] switch
            {
                'n' => text.Append('\n'),
                't' => text.Append('\t'),
                '\\' or '"' => text.Append(value[i]),
                _ => text.Append('\\').Append(value[i]),
            };
        }

        return text.ToString();
    }

    private static ReadOnlySpan<char> Content(string text, Token token) =>
        token.Quoted ? text.AsSpan(token.Start + 1, token.End - token.Start - 2) : text.AsSpan(token.Start, token.End - token.Start);

    private static bool KeyIs(string text, Token token, string key) =>
        Unescape(Content(text, token)).Equals(key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Non-recursive. Adds every block at <paramref name="appPath"/> to <paramref name="apps"/>; returns why the file can't be read, if it can't.</summary>
    private static string? Scan(string text, IReadOnlyList<string> appPath, List<AppBlock> apps)
    {
        // Per open block: whether it and every block around it follow appPath, and the app block it is, if any.
        var onPath = new Stack<bool>();
        var openApps = new Stack<AppBlock?>();
        Token? key = null;
        var position = 0;
        while (true)
        {
            var next = NextToken(text, ref position, out var token);
            if (next is TokenResult.End)
            {
                break;
            }

            if (next is TokenResult.Unterminated)
            {
                return "unterminated quoted string";
            }

            var parent = openApps.Count > 0 ? openApps.Peek() : null;
            switch (token.Kind)
            {
                // A [$WIN32]-style condition belongs to the key or value before it.
                case TokenKind.Text when !token.Quoted && text[token.Start] == '[':
                    break;
                case TokenKind.Text when key is null:
                    key = token;
                    break;
                case TokenKind.Text when key is { } valueKey:
                    if (parent is not null)
                    {
                        parent.LastKeyStart = valueKey.Start;
                        if (KeyIs(text, valueKey, SteamClientFiles.LaunchOptionsKey))
                        {
                            parent.Problem = parent.Value is null ? parent.Problem : "duplicate LaunchOptions";
                            parent.Value = token;
                        }
                    }
                    else if (IsAppKey(text, valueKey, onPath, appPath))
                    {
                        apps.Add(new AppBlock { Problem = "app entry is not a block" });
                    }

                    key = null;
                    break;
                case TokenKind.Open when key is { } blockKey:
                    if (onPath.Count == MaxDepth)
                    {
                        return "nested too deeply";
                    }

                    AppBlock? app = null;
                    if (parent is not null)
                    {
                        parent.LastKeyStart = blockKey.Start;
                        if (KeyIs(text, blockKey, SteamClientFiles.LaunchOptionsKey))
                        {
                            parent.Problem = "LaunchOptions is a block";
                        }
                    }
                    else if (IsAppKey(text, blockKey, onPath, appPath))
                    {
                        app = new AppBlock();
                        apps.Add(app);
                    }

                    var depth = onPath.Count;
                    onPath.Push((depth == 0 || onPath.Peek()) && depth < appPath.Count && KeyIs(text, blockKey, appPath[depth]));
                    openApps.Push(app);
                    key = null;
                    break;
                case TokenKind.Open:
                    return "block without a key";
                case TokenKind.Close when key is not null:
                    return "key without a value";
                case TokenKind.Close when onPath.Count == 0:
                    return "unbalanced closing brace";
                case TokenKind.Close:
                    onPath.Pop();
                    if (openApps.Pop() is { } closed)
                    {
                        closed.CloseStart = token.Start;
                    }

                    break;
            }
        }

        return key is not null ? "key without a value"
            : onPath.Count > 0 ? "unclosed block"
            : null;
    }

    private static bool IsAppKey(string text, Token key, Stack<bool> onPath, IReadOnlyList<string> appPath) =>
        onPath.Count == appPath.Count - 1
        && (onPath.Count == 0 || onPath.Peek())
        && KeyIs(text, key, appPath[^1]);

    private static TokenResult NextToken(string text, ref int position, out Token token)
    {
        token = default;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text.AsSpan(position).StartsWith("//", StringComparison.Ordinal))
            {
                var lineEnd = text.IndexOf('\n', position);
                position = lineEnd < 0 ? text.Length : lineEnd + 1;
            }
            else
            {
                break;
            }
        }

        if (position == text.Length)
        {
            return TokenResult.End;
        }

        var start = position;
        switch (text[position])
        {
            case '{':
                position++;
                token = new Token(TokenKind.Open, start, position, Quoted: false);
                return TokenResult.Token;
            case '}':
                position++;
                token = new Token(TokenKind.Close, start, position, Quoted: false);
                return TokenResult.Token;
            case '"':
                for (position++; position < text.Length; position++)
                {
                    if (text[position] == '\\')
                    {
                        position++;
                    }
                    else if (text[position] == '"')
                    {
                        position++;
                        token = new Token(TokenKind.Text, start, position, Quoted: true);
                        return TokenResult.Token;
                    }
                }

                return TokenResult.Unterminated;
            default:
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('"' or '{' or '}'))
                {
                    position++;
                }

                token = new Token(TokenKind.Text, start, position, Quoted: false);
                return TokenResult.Token;
        }
    }

    private enum TokenResult
    {
        Token,
        End,
        Unterminated,
    }

    private enum TokenKind
    {
        Text,
        Open,
        Close,
    }

    /// <param name="End">Exclusive; a quoted token includes its quotes.</param>
    private readonly record struct Token(TokenKind Kind, int Start, int End, bool Quoted);

    private sealed class AppBlock
    {
        public Token? Value { get; set; }

        public int CloseStart { get; set; }

        /// <summary>The last direct child key, whose indentation a new key copies.</summary>
        public int? LastKeyStart { get; set; }

        /// <summary>Why <c>LaunchOptions</c> can't be placed unambiguously.</summary>
        public string? Problem { get; set; }
    }
}
