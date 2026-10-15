using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services;

public sealed partial class JsonPreferenceStore(string filePath, ILogger logger) : IPreferenceStore
{
    private static readonly PreferencesFile Empty = new(null, null);

    // Saves are fire-and-forget; one at a time so the last choice is the one left on disk.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Every save rewrites the whole file from this, so saving one field never drops the other.
    private PreferencesFile? _file;

    public UserPreferences Load()
    {
        var file = Read();
        _file = file;

        var language = AppLanguages.FromCultureName(file.Language);
        if (language is null && file.Language is not null)
        {
            LogUnknownLanguage(logger, filePath, file.Language);
        }

        var theme = AppThemes.FromPreferenceName(file.Theme);
        if (theme is null && file.Theme is not null)
        {
            LogUnknownTheme(logger, filePath, file.Theme);
        }

        return new UserPreferences(language, theme);
    }

    public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default) =>
        SaveAsync(file => file with { Language = language.CultureName() }, ct);

    public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default) =>
        SaveAsync(file => file with { Theme = theme.PreferenceName() }, ct);

    private async Task SaveAsync(Func<PreferencesFile, PreferencesFile> update, CancellationToken ct)
    {
        await _saveGate.WaitAsync(ct);
        try
        {
            var file = update(_file ?? Read());
            _file = file;

            if (Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            // Write then rename, so a crash mid-write never leaves a truncated preference file.
            var tempPath = filePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, file, DesktopJsonContext.Default.PreferencesFile, ct);
            }

            File.Move(tempPath, filePath, overwrite: true);
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
            _saveGate.Release();
        }
    }

    private PreferencesFile Read()
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize(stream, DesktopJsonContext.Default.PreferencesFile) ?? Empty;
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
            return ReadFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ReadFailed(ex);
        }
        catch (JsonException ex)
        {
            return ReadFailed(ex);
        }
    }

    private PreferencesFile ReadFailed(Exception ex)
    {
        LogLoadFailed(logger, filePath, ex);
        return Empty;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preferences {Path} could not be read; using the OS language and the light theme")]
    private static partial void LogLoadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language preference {Path} holds an unsupported value '{Value}'; using the OS language")]
    private static partial void LogUnknownLanguage(ILogger logger, string path, string value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Theme preference {Path} holds an unsupported value '{Value}'; using the light theme")]
    private static partial void LogUnknownTheme(ILogger logger, string path, string value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preferences {Path} could not be saved")]
    private static partial void LogSaveFailed(ILogger logger, string path, Exception exception);
}
