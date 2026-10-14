using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Services;

public sealed partial class JsonLanguagePreferenceStore(string filePath, ILogger logger) : ILanguagePreferenceStore
{
    // Saves are fire-and-forget; one at a time so the last choice is the one left on disk.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public AppLanguage? Load()
    {
        LanguagePreference? preference;
        try
        {
            using var stream = File.OpenRead(filePath);
            preference = JsonSerializer.Deserialize(stream, DesktopJsonContext.Default.LanguagePreference);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException ex)
        {
            return LoadFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return LoadFailed(ex);
        }
        catch (JsonException ex)
        {
            return LoadFailed(ex);
        }

        var language = AppLanguages.FromCultureName(preference?.Language);
        if (language is null)
        {
            LogUnknownLanguage(logger, filePath, preference?.Language);
        }

        return language;
    }

    public async Task SaveAsync(AppLanguage language, CancellationToken ct = default)
    {
        await _saveGate.WaitAsync(ct);
        try
        {
            if (Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            // Write then rename, so a crash mid-write never leaves a truncated preference file.
            var tempPath = filePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new LanguagePreference(language.CultureName()), DesktopJsonContext.Default.LanguagePreference, ct);
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

    private AppLanguage? LoadFailed(Exception ex)
    {
        LogLoadFailed(logger, filePath, ex);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language preference {Path} could not be read; using the OS language")]
    private static partial void LogLoadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language preference {Path} holds an unsupported value '{Value}'; using the OS language")]
    private static partial void LogUnknownLanguage(ILogger logger, string path, string? value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language preference {Path} could not be saved")]
    private static partial void LogSaveFailed(ILogger logger, string path, Exception exception);
}
