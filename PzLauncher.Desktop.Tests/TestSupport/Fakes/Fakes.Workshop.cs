using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop.Ids;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

/// <summary>
/// Steam's local files are <see cref="Items"/>, already joined with their mods; the Web API answers from <see cref="Published"/> and
/// <see cref="Collections"/>. An item's state follows its installed manifest and the latest one known, the one a check or collection lookup
/// fetched before the item's own, as the real service does. The pre-launch check takes every installed item with a mod as used.
/// </summary>
public sealed class FakeWorkshopService : IWorkshopService, IWorkshopModsService
{
    private readonly Dictionary<ulong, WorkshopItemDetails> _fetched = [];

    public List<WorkshopModItem> Items { get; } = [];

    public List<Error> Warnings { get; } = [];

    public Dictionary<ulong, WorkshopItemDetails> Published { get; } = [];

    public Func<Error?> GetError { get; set; } = () => null;

    public Func<Error?> CheckError { get; set; } = () => null;

    /// <summary>Holds every check open, after recording it, until set.</summary>
    public TaskCompletionSource? CheckGate { get; set; }

    /// <summary>Reads of the subscriptions joined with mods.</summary>
    public List<(string Root, string ExtraGameArgs)> GetRequests { get; } = [];

    public List<(string Root, string ExtraGameArgs, string? Collection)> LaunchRequests { get; } = [];

    public List<IReadOnlyList<ulong>> ClassifyRequests { get; } = [];

    public List<IReadOnlyList<ulong>> CheckRequests { get; } = [];

    /// <summary>The token of each entry in <see cref="CheckRequests"/>.</summary>
    public List<CancellationToken> CheckTokens { get; } = [];

    /// <summary>Child ids in collection order, nested collections already expanded; the title is the collection's <see cref="Published"/> one.</summary>
    public Dictionary<ulong, ulong[]> Collections { get; } = [];

    public List<ulong> CollectionRequests { get; } = [];

    public Func<Error?> CollectionError { get; set; } = () => null;

    public SteamAccount Account { get; set; } = new(12345678, "Survivor");

    public Func<Error?> AccountError { get; set; } = () => null;

    public FakeWorkshopService WithItem(WorkshopModItem item)
    {
        Items.Add(item);
        return this;
    }

    public FakeWorkshopService WithPublished(ulong id, string title, string manifest)
    {
        Published[id] = TestWorkshop.Details(id, title, manifest);
        return this;
    }

    /// <summary>Steam installed the latest version, as a finished update leaves the library's acf.</summary>
    public void Install(ulong id, string manifest)
    {
        var index = Items.FindIndex(item => item.Item.Id == id);
        var item = Items[index].Item;
        Items[index] = Items[index] with { Item = item with { Installed = item.Installed! with { Manifest = manifest } } };
    }

    public Task<Result<WorkshopSubscriptions>> GetSubscriptionsAsync(string installRoot, CancellationToken ct = default) =>
        Task.FromResult(GetError() is { } error
            ? (Result<WorkshopSubscriptions>)error
            : new WorkshopSubscriptions([.. Joined().Select(item => item.Item)], [.. Warnings]));

    public Task<Result<WorkshopModSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        GetRequests.Add((installRoot, extraGameArgs));
        return Task.FromResult(GetError() is { } error ? (Result<WorkshopModSubscriptions>)error : new WorkshopModSubscriptions(Joined(), [.. Warnings]));
    }

    public async Task<Result<IReadOnlyList<WorkshopItem>>> FindOutdatedForLaunchAsync(
        string installRoot, string extraGameArgs, string? collection, CancellationToken ct = default)
    {
        LaunchRequests.Add((installRoot, extraGameArgs, collection));
        if (GetError() is { } error)
        {
            return error;
        }

        List<WorkshopItem> used =
        [
            .. Joined()
                .Where(item => item.Mods.Count > 0 && item.Item.State is WorkshopItemState.UpToDate or WorkshopItemState.NeedsUpdate)
                .Select(item => item.Item),
        ];
        if (used.Count == 0)
        {
            return Array.Empty<WorkshopItem>();
        }

        var latest = await CheckUpdatesAsync([.. used.Select(item => item.Id)], ct);
        if (!latest.IsSuccess)
        {
            return latest.Error;
        }

        var details = latest.Value.ToDictionary(item => item.Id);
        return used
            .Select(item => details.GetValueOrDefault(item.Id) is { } current ? item.WithLatest(current) : item)
            .Where(item => item.State == WorkshopItemState.NeedsUpdate)
            .ToList();
    }

    public Task<Result<IReadOnlyList<WorkshopIdKind>>> ClassifyAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default)
    {
        ClassifyRequests.Add(ids);
        if (CollectionError() is { } error)
        {
            return Task.FromResult<Result<IReadOnlyList<WorkshopIdKind>>>(error);
        }

        List<WorkshopIdKind> kinds =
        [
            .. ids.Distinct().Select(id => Collections.ContainsKey(id)
                ? new WorkshopIdKind(id, true, Published.GetValueOrDefault(id)?.Title)
                : new WorkshopIdKind(id, false, null)),
        ];
        return Task.FromResult<Result<IReadOnlyList<WorkshopIdKind>>>(kinds);
    }

    public async Task<Result<IReadOnlyList<WorkshopItemDetails>>> CheckUpdatesAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default)
    {
        CheckRequests.Add(ids);
        CheckTokens.Add(ct);
        if (CheckGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (CheckError() is { } error)
        {
            return error;
        }

        List<WorkshopItemDetails> details = [.. ids.Where(Published.ContainsKey).Select(id => Published[id])];
        foreach (var item in details)
        {
            _fetched[item.Id] = item;
        }

        return details;
    }

    public Task<Result<WorkshopCollection>> GetCollectionAsync(ulong id, CancellationToken ct = default)
    {
        CollectionRequests.Add(id);
        if (CollectionError() is { } error)
        {
            return Task.FromResult<Result<WorkshopCollection>>(error);
        }

        if (!Collections.TryGetValue(id, out var children))
        {
            return Task.FromResult<Result<WorkshopCollection>>(TestErrors.Create(ErrorCode.WorkshopCollectionNotFound));
        }

        List<WorkshopCollectionItem> items = [.. children.Select(child => new WorkshopCollectionItem(child, Published.GetValueOrDefault(child)))];
        foreach (var details in items.Select(item => item.Details).OfType<WorkshopItemDetails>())
        {
            _fetched[details.Id] = details;
        }

        return Task.FromResult<Result<WorkshopCollection>>(new WorkshopCollection(id, Published.GetValueOrDefault(id)?.Title, items));
    }

    public Task<Result<SteamAccount>> GetCurrentAccountAsync(CancellationToken ct = default) =>
        Task.FromResult(AccountError() is { } error ? (Result<SteamAccount>)error : Account);

    private List<WorkshopModItem> Joined() =>
    [
        .. Items.Select(item =>
            (_fetched.GetValueOrDefault(item.Item.Id) ?? item.Item.Latest) is { } latest ? item with { Item = item.Item.WithLatest(latest) } : item),
    ];
}

/// <summary>
/// Steam accepts every run, reports progress halfway for each id, then its outcome from <see cref="Failure"/>; every run is recorded.
/// </summary>
public sealed class FakeWorkshopActionRunner : IWorkshopActionRunner
{
    public List<(string Root, WorkshopActionRequest Request)> Runs { get; } = [];

    /// <summary>Returned instead of an outcome, after the started event.</summary>
    public Func<WorkshopActionRequest, Error?> RunError { get; set; } = _ => null;

    public Func<ulong, WorkshopItemFailure?> Failure { get; set; } = _ => null;

    /// <summary>Called for each id that succeeds, before its finished event.</summary>
    public Action<WorkshopAction, ulong>? Succeeded { get; set; }

    /// <summary>Holds every run open after the progress events, until set.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<string> ListRuns { get; } = [];

    public WorkshopUserCollections Collections { get; set; } = new(12345678, [], []);

    public Func<Error?> ListError { get; set; } = () => null;

    /// <summary>Holds every collection list open until set.</summary>
    public TaskCompletionSource? ListGate { get; set; }

    public async Task<Result<WorkshopActionOutcome>> RunAsync(
        string installRoot, WorkshopActionRequest request, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default)
    {
        Runs.Add((installRoot, request));
        progress?.Report(new WorkshopActionStarted());
        if (RunError(request) is { } error)
        {
            return error;
        }

        foreach (var id in request.Ids)
        {
            progress?.Report(new WorkshopItemProgress(id, 50, 100));
        }

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        List<WorkshopItemOutcome> outcomes = [];
        foreach (var id in request.Ids)
        {
            var failure = Failure(id);
            if (failure is null)
            {
                Succeeded?.Invoke(request.Action, id);
            }

            var outcome = new WorkshopItemOutcome(id, failure, failure is null ? 0 : 15);
            outcomes.Add(outcome);
            progress?.Report(new WorkshopItemFinished(outcome));
        }

        return new WorkshopActionOutcome(outcomes);
    }

    public async Task<Result<WorkshopUserCollections>> ListCollectionsAsync(
        string installRoot, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default)
    {
        ListRuns.Add(installRoot);
        if (ListGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return ListError() is { } error ? error : Collections;
    }
}

public static class TestWorkshop
{
    public static readonly DateTimeOffset Subscribed = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Updated = new(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);

    /// <param name="manifest">The installed copy's; <c>null</c> for an item Steam hasn't installed.</param>
    public static WorkshopModItem Item(
        ulong id,
        string? manifest = "100",
        string[]? mods = null,
        string[]? collections = null,
        WorkshopItemState? state = null,
        WorkshopItemDetails? latest = null,
        long size = 3 * 1024 * 1024) =>
        new(
            new WorkshopItem(
                id,
                Subscribed,
                state == WorkshopItemState.DisabledLocally,
                manifest is null ? null : new WorkshopInstall(@$"D:\SteamLibrary\steamapps\workshop\content\108600\{id}", manifest, Updated, size),
                latest,
                state ?? (manifest is null ? WorkshopItemState.NotInstalled : WorkshopItemState.UpToDate)),
            [.. (mods ?? []).Select(mod => TestMods.Mod(mod, workshopId: id))],
            collections ?? []);

    public static WorkshopItemDetails Details(ulong id, string title, string manifest) => new(id, title, manifest, Updated, 3 * 1024 * 1024);

    public static WorkshopLaunchCheck LaunchCheck(IWorkshopModsService workshop, TimeProvider? time = null) =>
        new(workshop, time ?? new ManualTimeProvider(), NullLogger<WorkshopLaunchCheck>.Instance);

    public static WorkshopCollectionHistory History(FakePreferenceStore? store = null, params SavedWorkshopEntry[] saved) =>
        new(store ?? new FakePreferenceStore(), new UserPreferences(null, null, null) { WorkshopCollections = saved });

    public static WorkshopFavorites Favorites(FakePreferenceStore? store = null, SavedWorkshopFavorites? saved = null) =>
        new(store ?? new FakePreferenceStore(), new UserPreferences(null, null, null) { WorkshopFavorites = saved ?? SavedWorkshopFavorites.None });

    public static LocalCollectionStore LocalCollectionStore(FakeModLibraryFile? file = null, params SavedLocalCollection[] saved) =>
        new(file ?? new FakeModLibraryFile(), ModLibrary.Empty with { LocalCollections = saved });

    public static ModpackStore ModpackStoreOf(
        LocalCollectionStore localCollections,
        FakeModLibraryFile? file = null,
        IReadOnlyDictionary<string, IReadOnlyList<Modpack>>? saved = null,
        bool localCollectionsComplete = true,
        StringComparison pathComparison = StringComparison.OrdinalIgnoreCase) =>
        new(
            file ?? new FakeModLibraryFile(),
            ModLibrary.Empty with
            {
                Modpacks = (saved ?? new Dictionary<string, IReadOnlyList<Modpack>>())
                    .ToDictionary(entry => entry.Key, IReadOnlyList<SavedModpack> (entry) => [.. entry.Value.Select(modpack => modpack.ToSaved())]),
                LocalCollectionsComplete = localCollectionsComplete,
            },
            new FakePlatformEnvironment { PathComparison = pathComparison },
            localCollections);

    public static WorkshopUserCollectionsCache UserCollections(FakePreferenceStore? store = null, SavedWorkshopUserCollections? saved = null, TimeProvider? time = null) =>
        new(store ?? new FakePreferenceStore(), new UserPreferences(null, null, null) { WorkshopUserCollections = saved }, time ?? new ManualTimeProvider());
}
