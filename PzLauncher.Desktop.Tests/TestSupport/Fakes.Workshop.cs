using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>
/// Steam's local files are <see cref="Items"/>; the Web API answers from <see cref="Published"/> and <see cref="Collections"/>. An item's
/// state follows its installed manifest and the latest one known, the one a check or collection lookup fetched before the item's own, as
/// the real service does.
/// </summary>
public sealed class FakeWorkshopService : IWorkshopService
{
    private readonly Dictionary<ulong, WorkshopItemDetails> _fetched = [];

    public List<WorkshopItem> Items { get; } = [];

    public List<Error> Warnings { get; } = [];

    public Dictionary<ulong, WorkshopItemDetails> Published { get; } = [];

    public Func<Error?> GetError { get; set; } = () => null;

    public Func<Error?> CheckError { get; set; } = () => null;

    /// <summary>Holds every check open, after recording it, until set.</summary>
    public TaskCompletionSource? CheckGate { get; set; }

    public List<(string Root, string ExtraGameArgs)> GetRequests { get; } = [];

    public List<IReadOnlyList<ulong>> CheckRequests { get; } = [];

    /// <summary>Child ids in collection order, nested collections already expanded; the title is the collection's <see cref="Published"/> one.</summary>
    public Dictionary<ulong, ulong[]> Collections { get; } = [];

    public List<ulong> CollectionRequests { get; } = [];

    public Func<Error?> CollectionError { get; set; } = () => null;

    public FakeWorkshopService WithItem(WorkshopItem item)
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
        var index = Items.FindIndex(item => item.Id == id);
        Items[index] = Items[index] with { Installed = Items[index].Installed! with { Manifest = manifest } };
    }

    public Task<Result<WorkshopSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        GetRequests.Add((installRoot, extraGameArgs));
        if (GetError() is { } error)
        {
            return Task.FromResult<Result<WorkshopSubscriptions>>(error);
        }

        List<WorkshopItem> items = [.. Items.Select(item => (_fetched.GetValueOrDefault(item.Id) ?? item.Latest) is { } latest ? WithLatest(item, latest) : item)];
        return Task.FromResult<Result<WorkshopSubscriptions>>(new WorkshopSubscriptions(items, [.. Warnings]));
    }

    public async Task<Result<IReadOnlyList<WorkshopItemDetails>>> CheckUpdatesAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default)
    {
        CheckRequests.Add(ids);
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

    private static WorkshopItem WithLatest(WorkshopItem item, WorkshopItemDetails latest) => item with
    {
        Latest = latest,
        State = item.State switch
        {
            WorkshopItemState.UpToDate or WorkshopItemState.NeedsUpdate => item.Installed!.Manifest == latest.Manifest
                ? WorkshopItemState.UpToDate
                : WorkshopItemState.NeedsUpdate,
            WorkshopItemState.NotInstalled or WorkshopItemState.DisabledLocally or WorkshopItemState.NotSubscribed => item.State,
        },
    };
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
}

public static class TestWorkshop
{
    public static readonly DateTimeOffset Subscribed = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Updated = new(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);

    /// <param name="manifest">The installed copy's; <c>null</c> for an item Steam hasn't installed.</param>
    public static WorkshopItem Item(
        ulong id,
        string? manifest = "100",
        string[]? mods = null,
        string[]? collections = null,
        WorkshopItemState? state = null,
        WorkshopItemDetails? latest = null,
        long size = 3 * 1024 * 1024) =>
        new(
            id,
            Subscribed,
            state == WorkshopItemState.DisabledLocally,
            manifest is null ? null : new WorkshopInstall(@$"D:\SteamLibrary\steamapps\workshop\content\108600\{id}", manifest, Updated, size),
            latest,
            [.. (mods ?? []).Select(mod => TestMods.Mod(mod, workshopId: id.ToString(System.Globalization.CultureInfo.InvariantCulture)))],
            collections ?? [],
            state ?? (manifest is null ? WorkshopItemState.NotInstalled : WorkshopItemState.UpToDate));

    public static WorkshopItemDetails Details(ulong id, string title, string manifest) => new(id, title, manifest, Updated, 3 * 1024 * 1024);

    public static WorkshopLaunchCheck LaunchCheck(IWorkshopService workshop, IModCollectionService collections, TimeProvider? time = null) =>
        new(workshop, collections, time ?? new ManualTimeProvider(), NullLogger<WorkshopLaunchCheck>.Instance);

    public static WorkshopCollectionHistory History(FakePreferenceStore? store = null, params SavedWorkshopCollection[] saved) =>
        new(store ?? new FakePreferenceStore(), new UserPreferences(null, null, null) { WorkshopCollections = saved });
}
