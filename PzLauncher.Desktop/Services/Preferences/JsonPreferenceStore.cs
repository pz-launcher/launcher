using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services.Preferences;

public sealed partial class JsonPreferenceStore(string filePath, ILogger logger, ISecretProtector secrets) : IPreferenceStore
{
    private static readonly PreferencesFile Empty = new(null, null, null, null, null, null, null, null, null, null);

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

        var window = ToPlacement(file.Window);
        if (window is null && file.Window is not null)
        {
            LogInvalidWindowPlacement(logger, filePath);
        }

        var javaHomes = ToManualJavaHomes(file.JavaRuntimes);
        if (javaHomes.Count != (file.JavaRuntimes?.Count ?? 0))
        {
            LogInvalidJavaRuntimes(logger, filePath);
        }

        var installs = ToInstallOverrides(file.Installs);
        if (installs.Count != (file.Installs?.Count ?? 0))
        {
            LogInvalidInstallOverrides(logger, filePath);
        }

        var channels = ToZombieBuddyChannels(file.ZombieBuddyChannels);
        if (channels.Count != (file.ZombieBuddyChannels?.Count ?? 0))
        {
            LogInvalidZombieBuddyChannels(logger, filePath);
        }

        var collections = ToDefaultModCollections(file.DefaultModCollections);
        if (collections.Count != (file.DefaultModCollections?.Count ?? 0))
        {
            LogInvalidDefaultModCollections(logger, filePath);
        }

        var updateSource = file.UpdateSource is { ValueKind: JsonValueKind.String } source
            ? UpdateSourcePreferences.FromPreferenceName(source.GetString())
            : null;
        if (updateSource is null && file.UpdateSource is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidUpdateSource(logger, filePath);
        }

        var autoCheckUpdates = file.AutoCheckUpdates?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => (bool?)null,
        };
        if (autoCheckUpdates is null && file.AutoCheckUpdates is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidAutoCheckUpdates(logger, filePath);
        }

        return new UserPreferences(language, theme, window)
        {
            UpdateSource = updateSource,
            AutoCheckUpdates = autoCheckUpdates,
            Proxy = ToProxy(file.Proxy),
            ManualJavaHomes = javaHomes,
            InstallOverrides = installs,
            ZombieBuddyChannels = channels,
            DefaultModCollections = collections,
        };
    }

    public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default) =>
        SaveAsync(file => file with { Language = language.CultureName() }, ct);

    public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default) =>
        SaveAsync(file => file with { Theme = theme.PreferenceName() }, ct);

    public Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default)
    {
        var window = new WindowFile(placement.X, placement.Y, placement.Width, placement.Height, placement.IsMaximized);
        return SaveAsync(file => file with { Window = window }, ct);
    }

    public Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default)
    {
        JsonElement[] entries = [.. homes.Select(home => JsonSerializer.SerializeToElement(home, DesktopJsonContext.Default.String))];
        return SaveAsync(file => file with { JavaRuntimes = entries }, ct);
    }

    public Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(
            new InstallOverridesFile(
                overrides.JavaHome,
                overrides.MaxMemoryMb,
                overrides.MinMemoryMb,
                overrides.ExtraJvmArgs,
                overrides.ExtraGameArgs,
                overrides.GarbageCollector?.PreferenceName()),
            DesktopJsonContext.Default.InstallOverridesFile);
        return SaveAsync(
            file => file with
            {
                Installs = new Dictionary<string, JsonElement>(file.Installs ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal)
                {
                    [installRoot] = entry,
                },
            },
            ct);
    }

    public Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(channel.PreferenceName(), DesktopJsonContext.Default.String);
        return SaveAsync(
            file => file with
            {
                ZombieBuddyChannels = new Dictionary<string, JsonElement>(
                    file.ZombieBuddyChannels ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal)
                {
                    [installRoot] = entry,
                },
            },
            ct);
    }

    public Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default) =>
        SaveAsync(
            file =>
            {
                var entries = new Dictionary<string, JsonElement>(
                    file.DefaultModCollections ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal);
                if (name is null)
                {
                    entries.Remove(installRoot);
                }
                else
                {
                    entries[installRoot] = JsonSerializer.SerializeToElement(name, DesktopJsonContext.Default.String);
                }

                return file with { DefaultModCollections = entries };
            },
            ct);

    public Task SaveUpdateSourceAsync(UpdateSource source, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(source.PreferenceName(), DesktopJsonContext.Default.String);
        return SaveAsync(file => file with { UpdateSource = entry }, ct);
    }

    public Task SaveAutoCheckUpdatesAsync(bool enabled, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(enabled, DesktopJsonContext.Default.Boolean);
        return SaveAsync(file => file with { AutoCheckUpdates = entry }, ct);
    }

    public Task SaveProxyAsync(SavedProxy proxy, CancellationToken ct = default)
    {
        string? password = null;
        if (proxy.Password is { Length: > 0 } secret)
        {
            password = secrets.Protect(secret);
            if (password is null)
            {
                LogProxyPasswordNotProtected(logger, filePath);
            }
        }

        var entry = JsonSerializer.SerializeToElement(
            new ProxyFile(
                proxy.Mode.PreferenceName(),
                proxy.Host is { Length: > 0 } host ? host : null,
                proxy.Port,
                proxy.Username is { Length: > 0 } username ? username : null,
                password),
            DesktopJsonContext.Default.ProxyFile);
        return SaveAsync(file => file with { Proxy = entry }, ct);
    }

    private SavedProxy? ToProxy(JsonElement? element)
    {
        if (element is not { ValueKind: not JsonValueKind.Null } value)
        {
            return null;
        }

        ProxyFile? entry;
        try
        {
            entry = value.Deserialize(DesktopJsonContext.Default.ProxyFile);
        }
        catch (JsonException)
        {
            entry = null;
        }

        if (entry is null || ProxyModePreferences.FromPreferenceName(entry.Mode) is not { } mode)
        {
            LogInvalidProxy(logger, filePath);
            return null;
        }

        var host = entry.Host is { } savedHost && ProxySettingsValidation.NormalizeHost(savedHost) is not null ? savedHost : null;
        var port = entry.Port is { } savedPort && ProxySettingsValidation.IsValidPort(savedPort) ? savedPort : (int?)null;
        if ((host is null && entry.Host is not null) || (port is null && entry.Port is not null))
        {
            LogInvalidProxyAddress(logger, filePath);
        }

        string? password = null;
        if (entry.Password is { } stored)
        {
            password = secrets.Unprotect(stored);
            if (password is null)
            {
                LogProxyPasswordUnreadable(logger, filePath);
            }
        }

        return new SavedProxy(mode, host, port, entry.Username is { Length: > 0 } username ? username : null, password);
    }

    private static List<string> ToManualJavaHomes(IReadOnlyList<JsonElement>? entries) =>
    [
        .. (entries ?? [])
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString() ?? "")
            .Where(IsAbsolutePath),
    ];

    private static Dictionary<string, GameLaunchOverrides> ToInstallOverrides(IReadOnlyDictionary<string, JsonElement>? entries)
    {
        var result = new Dictionary<string, GameLaunchOverrides>(StringComparer.Ordinal);
        foreach (var (root, element) in entries ?? new Dictionary<string, JsonElement>())
        {
            if (IsAbsolutePath(root) && ToOverrides(element) is { } overrides)
            {
                result[root] = overrides;
            }
        }

        return result;
    }

    private static Dictionary<string, ZombieBuddyChannel> ToZombieBuddyChannels(IReadOnlyDictionary<string, JsonElement>? entries)
    {
        var result = new Dictionary<string, ZombieBuddyChannel>(StringComparer.Ordinal);
        foreach (var (root, element) in entries ?? new Dictionary<string, JsonElement>())
        {
            if (IsAbsolutePath(root)
                && element.ValueKind == JsonValueKind.String
                && ZombieBuddyChannelPreferences.FromPreferenceName(element.GetString()) is { } channel)
            {
                result[root] = channel;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ToDefaultModCollections(IReadOnlyDictionary<string, JsonElement>? entries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (root, element) in entries ?? new Dictionary<string, JsonElement>())
        {
            if (IsAbsolutePath(root) && element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } name)
            {
                result[root] = name;
            }
        }

        return result;
    }

    private static GameLaunchOverrides? ToOverrides(JsonElement element)
    {
        InstallOverridesFile? entry;
        try
        {
            entry = element.Deserialize(DesktopJsonContext.Default.InstallOverridesFile);
        }
        catch (JsonException)
        {
            return null;
        }

        return entry is { MaxMemoryMb: null or > 0, MinMemoryMb: null or > 0 }
               && (entry.JavaHome is null || IsAbsolutePath(entry.JavaHome))
               && !(entry.MinMemoryMb > entry.MaxMemoryMb)
            ? new GameLaunchOverrides(
                entry.JavaHome,
                entry.MaxMemoryMb,
                entry.MinMemoryMb,
                entry.JvmArgs ?? "",
                entry.GameArgs ?? "",
                GarbageCollectorPreferences.FromPreferenceName(entry.Gc))
            : null;
    }

    private static bool IsAbsolutePath(string path) =>
        path.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(path);

    private static WindowPlacement? ToPlacement(WindowFile? window) =>
        window is { X: { } x, Y: { } y, Width: { } width, Height: { } height, Maximized: { } maximized }
        && double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0
            ? new WindowPlacement(x, y, width, height, maximized)
            : null;

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preferences {Path} could not be read; using the OS language, the light theme and the default window placement")]
    private static partial void LogLoadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language preference {Path} holds an unsupported value '{Value}'; using the OS language")]
    private static partial void LogUnknownLanguage(ILogger logger, string path, string value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Theme preference {Path} holds an unsupported value '{Value}'; using the light theme")]
    private static partial void LogUnknownTheme(ILogger logger, string path, string value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window placement preference {Path} is incomplete or out of range; using the default size and position")]
    private static partial void LogInvalidWindowPlacement(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Java runtime preference {Path} holds entries that aren't absolute paths; ignoring them")]
    private static partial void LogInvalidJavaRuntimes(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Install settings preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidInstallOverrides(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ZombieBuddy channel preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidZombieBuddyChannels(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Default mod collection preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidDefaultModCollections(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update source preference {Path} holds an unsupported value; using automatic")]
    private static partial void LogInvalidUpdateSource(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic update check preference {Path} is not true or false; checking automatically")]
    private static partial void LogInvalidAutoCheckUpdates(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy preference {Path} is not valid; using the system proxy")]
    private static partial void LogInvalidProxy(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy preference {Path} holds an invalid host or port; ignoring it")]
    private static partial void LogInvalidProxyAddress(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy password in {Path} could not be decrypted (another user, machine or OS); it must be entered again")]
    private static partial void LogProxyPasswordUnreadable(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy password could not be encrypted; saving {Path} without it")]
    private static partial void LogProxyPasswordNotProtected(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preferences {Path} could not be saved")]
    private static partial void LogSaveFailed(ILogger logger, string path, Exception exception);
}
