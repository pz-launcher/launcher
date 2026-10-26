using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences.Secrets;
using PzLauncher.Desktop.Services.Shell.Lifetime;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;
using static PzLauncher.Desktop.Services.Preferences.PreferenceJson;

namespace PzLauncher.Desktop.Services.Preferences;

public sealed partial class JsonPreferenceStore(string filePath, ILogger logger, ISecretProtector secrets) : IPreferenceStore
{
    private static readonly PreferencesFile Empty = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    // Saves are fire-and-forget; one at a time so the last choice is the one left on disk.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Every save rewrites the whole file from this, so saving one field never drops another, unknown ones included.
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

        var installs = ReadPerInstall<GameLaunchOverrides>(file.Installs, TryReadOverrides);
        if (installs.Count != (file.Installs?.Count ?? 0))
        {
            LogInvalidInstallOverrides(logger, filePath);
        }

        var channels = ReadPerInstall<ZombieBuddyChannel>(file.ZombieBuddyChannels, TryReadChannel);
        if (channels.Count != (file.ZombieBuddyChannels?.Count ?? 0))
        {
            LogInvalidZombieBuddyChannels(logger, filePath);
        }

        var collections = ReadPerInstall<string>(file.DefaultModCollections, TryReadCollectionName);
        if (collections.Count != (file.DefaultModCollections?.Count ?? 0))
        {
            LogInvalidDefaultModCollections(logger, filePath);
        }

        var updateSource = ToNamed(file.UpdateSource, UpdateSourcePreferences.FromPreferenceName);
        if (updateSource is null && file.UpdateSource is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidUpdateSource(logger, filePath);
        }

        var autoCheckUpdates = ToBool(file.AutoCheckUpdates);
        if (autoCheckUpdates is null && file.AutoCheckUpdates is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidAutoCheckUpdates(logger, filePath);
        }

        var steamHookReminder = ToBool(file.SteamHookReminder);
        if (steamHookReminder is null && file.SteamHookReminder is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidSteamHookReminder(logger, filePath);
        }

        var afterLaunch = ToNamed(file.AfterLaunch, AfterLaunchPreferences.FromPreferenceName);
        if (afterLaunch is null && file.AfterLaunch is { ValueKind: not JsonValueKind.Null })
        {
            LogInvalidAfterLaunch(logger, filePath);
        }

        var workshopCollections = ToWorkshopEntries(file.WorkshopCollections, out var workshopCollectionsValid);
        if (!workshopCollectionsValid)
        {
            LogInvalidWorkshopCollections(logger, filePath);
        }

        var userCollections = ToWorkshopUserCollections(file.WorkshopUserCollections, out var userCollectionsValid);
        if (!userCollectionsValid)
        {
            LogInvalidWorkshopUserCollections(logger, filePath);
        }

        var favorites = ToWorkshopFavorites(file.WorkshopFavorites, out var favoritesValid);
        if (!favoritesValid)
        {
            LogInvalidWorkshopFavorites(logger, filePath);
        }

        return new UserPreferences(language, theme, window)
        {
            UpdateSource = updateSource,
            AutoCheckUpdates = autoCheckUpdates,
            SteamHookReminder = steamHookReminder,
            Proxy = ToProxy(file.Proxy),
            ManualJavaHomes = javaHomes,
            InstallOverrides = installs,
            ZombieBuddyChannels = channels,
            DefaultModCollections = collections,
            WorkshopCollections = workshopCollections,
            WorkshopUserCollections = userCollections,
            WorkshopFavorites = favorites,
            AfterLaunch = afterLaunch,
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
        return SaveAsync(file => file with { Installs = WithPerInstall(file.Installs, installRoot, entry) }, ct);
    }

    public Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default)
    {
        var entry = NameElement(channel.PreferenceName());
        return SaveAsync(file => file with { ZombieBuddyChannels = WithPerInstall(file.ZombieBuddyChannels, installRoot, entry) }, ct);
    }

    public Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default)
    {
        JsonElement? entry = name is null ? null : JsonSerializer.SerializeToElement(name, DesktopJsonContext.Default.String);
        return SaveAsync(file => file with { DefaultModCollections = WithPerInstall(file.DefaultModCollections, installRoot, entry) }, ct);
    }

    public Task SaveUpdateSourceAsync(UpdateSource source, CancellationToken ct = default)
    {
        var entry = NameElement(source.PreferenceName());
        return SaveAsync(file => file with { UpdateSource = entry }, ct);
    }

    public Task SaveAutoCheckUpdatesAsync(bool enabled, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(enabled, DesktopJsonContext.Default.Boolean);
        return SaveAsync(file => file with { AutoCheckUpdates = entry }, ct);
    }

    public Task SaveSteamHookReminderAsync(bool remind, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(remind, DesktopJsonContext.Default.Boolean);
        return SaveAsync(file => file with { SteamHookReminder = entry }, ct);
    }

    public Task SaveAfterLaunchAsync(AfterLaunchAction action, CancellationToken ct = default)
    {
        var entry = NameElement(action.PreferenceName());
        return SaveAsync(file => file with { AfterLaunch = entry }, ct);
    }

    public Task SaveWorkshopCollectionsAsync(IReadOnlyList<SavedWorkshopEntry> collections, CancellationToken ct = default)
    {
        var element = ToElement(collections);
        return SaveAsync(file => file with { WorkshopCollections = element }, ct);
    }

    public Task SaveWorkshopFavoritesAsync(SavedWorkshopFavorites favorites, CancellationToken ct = default)
    {
        var element = JsonSerializer.SerializeToElement(
            new WorkshopFavoritesFile(ToElement(favorites.Collections), ToElement(favorites.Items)), DesktopJsonContext.Default.WorkshopFavoritesFile);
        return SaveAsync(file => file with { WorkshopFavorites = element }, ct);
    }

    public Task SaveWorkshopUserCollectionsAsync(SavedWorkshopUserCollections collections, CancellationToken ct = default)
    {
        var entry = JsonSerializer.SerializeToElement(
            new WorkshopUserCollectionsFile(
                collections.Collections.AccountId,
                collections.FetchedAt,
                [.. collections.Collections.Favorited.Select(ToElement)],
                [.. collections.Collections.Published.Select(ToElement)]),
            DesktopJsonContext.Default.WorkshopUserCollectionsFile);
        return SaveAsync(file => file with { WorkshopUserCollections = entry }, ct);

        static JsonElement ToElement(WorkshopCollectionSummary summary) =>
            JsonSerializer.SerializeToElement(
                new WorkshopUserCollectionFile(summary.Id.ToString(CultureInfo.InvariantCulture), summary.Title, summary.ChildCount),
                DesktopJsonContext.Default.WorkshopUserCollectionFile);
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

        var entry = TryDeserialize(value, DesktopJsonContext.Default.ProxyFile);
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

    private static bool? ToBool(JsonElement? element) =>
        element?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static T? ToNamed<T>(JsonElement? element, Func<string?, T?> fromPreferenceName)
        where T : struct =>
        element is { ValueKind: JsonValueKind.String } name ? fromPreferenceName(name.GetString()) : null;

    private static JsonElement NameElement(string preferenceName) => TextElement(preferenceName);

    private static List<string> ToManualJavaHomes(IReadOnlyList<JsonElement>? entries) =>
    [
        .. (entries ?? [])
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString() ?? "")
            .Where(IsAbsolutePath),
    ];

    // A non-object loads none; a list that is missing loads empty, one that isn't an array or a bad entry is dropped.
    private static SavedWorkshopFavorites ToWorkshopFavorites(JsonElement? element, out bool valid)
    {
        valid = true;
        if (element is not { ValueKind: not JsonValueKind.Null } value)
        {
            return SavedWorkshopFavorites.None;
        }

        if (value.ValueKind != JsonValueKind.Object || TryDeserialize(value, DesktopJsonContext.Default.WorkshopFavoritesFile) is not { } file)
        {
            valid = false;
            return SavedWorkshopFavorites.None;
        }

        var collections = ToWorkshopEntries(file.Collections, out var collectionsValid);
        var items = ToWorkshopEntries(file.Items, out var itemsValid);
        valid = collectionsValid && itemsValid;
        return new SavedWorkshopFavorites(collections, items);
    }

    // A missing account, time or list makes the whole entry invalid; a bad or repeated list entry is dropped.
    private static SavedWorkshopUserCollections? ToWorkshopUserCollections(JsonElement? element, out bool valid)
    {
        valid = true;
        if (element is not { ValueKind: not JsonValueKind.Null } value)
        {
            return null;
        }

        if (TryDeserialize(value, DesktopJsonContext.Default.WorkshopUserCollectionsFile) is not
            { AccountId: { } accountId and not 0, FetchedAt: { } fetchedAt, Favorited: { } favoritedEntries, Published: { } publishedEntries })
        {
            valid = false;
            return null;
        }

        var favorited = ReadById<WorkshopCollectionSummary>(favoritedEntries, TryReadCollectionSummary, summary => summary.Id, ref valid);
        var published = ReadById<WorkshopCollectionSummary>(publishedEntries, TryReadCollectionSummary, summary => summary.Id, ref valid);
        return new SavedWorkshopUserCollections(fetchedAt, new WorkshopUserCollections(accountId, favorited, published));
    }

    private static bool TryReadCollectionSummary(JsonElement element, [MaybeNullWhen(false)] out WorkshopCollectionSummary summary)
    {
        summary = element.ValueKind == JsonValueKind.Object
            && TryDeserialize(element, DesktopJsonContext.Default.WorkshopUserCollectionFile) is { ChildCount: { } childCount } read
            && TryReadWorkshopId(read.Id, out var id)
                ? new WorkshopCollectionSummary(id, read.Title is { Length: > 0 } title ? title : null, childCount)
                : null;
        return summary is not null;
    }

    // A null entry removes the install's value.
    private static Dictionary<string, JsonElement> WithPerInstall(
        IReadOnlyDictionary<string, JsonElement>? entries, string installRoot, JsonElement? entry)
    {
        var result = new Dictionary<string, JsonElement>(entries ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal);
        if (entry is { } value)
        {
            result[installRoot] = value;
        }
        else
        {
            result.Remove(installRoot);
        }

        return result;
    }

    private static bool TryReadOverrides(JsonElement element, [MaybeNullWhen(false)] out GameLaunchOverrides overrides) =>
        (overrides = ToOverrides(element)) is not null;

    private static bool TryReadChannel(JsonElement element, out ZombieBuddyChannel channel)
    {
        var read = element.ValueKind == JsonValueKind.String ? ZombieBuddyChannelPreferences.FromPreferenceName(element.GetString()) : null;
        channel = read.GetValueOrDefault();
        return read is not null;
    }

    private static bool TryReadCollectionName(JsonElement element, [MaybeNullWhen(false)] out string name)
    {
        name = element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } read ? read : null;
        return name is not null;
    }

    private static GameLaunchOverrides? ToOverrides(JsonElement element) =>
        TryDeserialize(element, DesktopJsonContext.Default.InstallOverridesFile) is { MaxMemoryMb: null or > 0, MinMemoryMb: null or > 0 } entry
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
            await WriteAsync(filePath, file, DesktopJsonContext.Default.PreferencesFile, ct);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam hook reminder preference {Path} is not true or false; reminding")]
    private static partial void LogInvalidSteamHookReminder(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "After-launch preference {Path} holds an unsupported value; asking at the next launch")]
    private static partial void LogInvalidAfterLaunch(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop collection list preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidWorkshopCollections(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam collection list preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidWorkshopUserCollections(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop favorites preference {Path} holds entries that aren't valid; ignoring them")]
    private static partial void LogInvalidWorkshopFavorites(ILogger logger, string path);

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
