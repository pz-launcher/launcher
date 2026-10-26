using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Mods.Library;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

public sealed class FakeModLibraryFile : IModLibraryFile
{
    public List<IReadOnlyList<SavedLocalCollection>> SavedLocalCollections { get; } = [];

    public List<IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>>> SavedModpacks { get; } = [];

    public ModLibrary Load() => ModLibrary.Empty;

    public Task SaveLocalCollectionsAsync(IReadOnlyList<SavedLocalCollection> collections, CancellationToken ct = default)
    {
        SavedLocalCollections.Add(collections);
        return Task.CompletedTask;
    }

    public Task SaveModpacksAsync(IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>> modpacks, CancellationToken ct = default)
    {
        SavedModpacks.Add(modpacks);
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeModCollectionDefaultsService : IModCollectionDefaultsService
{
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string installRoot) => _defaults.GetValueOrDefault(installRoot);

    public void Save(string installRoot, string name) => _defaults[installRoot] = name;

    public void Clear(string installRoot) => _defaults.Remove(installRoot);
}

/// <summary>
/// An in-memory cache folder: saves, deletes and applies change <see cref="Collections"/> and <see cref="ActiveIds"/> as
/// the real service changes the files, and every call is recorded.
/// </summary>
public sealed class FakeModCollectionService : IModCollectionService
{
    public List<InstalledMod> InstalledMods { get; } = [];

    /// <summary>Name and ids in file order.</summary>
    public List<(string Name, IReadOnlyList<string> ModIds)> Collections { get; } = [];

    public List<string> ActiveIds { get; set; } = [];

    public Func<Error?> GetError { get; set; } = () => null;

    public Func<SaveModCollectionRequest, Error?> SaveError { get; set; } = _ => null;

    /// <summary>Holds every save open, after recording it, until set.</summary>
    public TaskCompletionSource? SaveGate { get; set; }

    public Func<string, Error?> ApplyError { get; set; } = _ => null;

    public List<(string Root, string ExtraGameArgs)> GetRequests { get; } = [];

    public List<SaveModCollectionRequest> Saves { get; } = [];

    public List<string> Deletes { get; } = [];

    public List<(string Root, string ExtraGameArgs, string Name)> Applies { get; } = [];

    public FakeModCollectionService WithMods(params InstalledMod[] mods)
    {
        InstalledMods.AddRange(mods);
        return this;
    }

    public FakeModCollectionService WithCollection(string name, params string[] ids)
    {
        Collections.Add((name, ids));
        return this;
    }

    public IReadOnlyList<string>? IdsOf(string name) =>
        Collections.FindIndex(collection => collection.Name == name) is var index and >= 0 ? Collections[index].ModIds : null;

    public Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        GetRequests.Add((installRoot, extraGameArgs));
        if (GetError() is { } error)
        {
            return Task.FromResult<Result<ModCollectionState>>(error);
        }

        var installed = InstalledMods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var active = ActiveIds.Where(installed.Contains).ToHashSet(StringComparer.Ordinal);
        List<ModCollection> collections =
        [
            .. Collections.Select(collection => ModCollection.Create(collection.Name, collection.ModIds, InstalledMods, ActiveIds)),
        ];
        return Task.FromResult<Result<ModCollectionState>>(new ModCollectionState(
            @"C:\Users\Test\Zomboid", collections, [.. InstalledMods], [.. ActiveIds], active.Count == 0 || collections.Any(collection => collection.IsActive)));
    }

    public async Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default)
    {
        Saves.Add(request);
        if (SaveGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (SaveError(request) is { } error)
        {
            return error;
        }

        if (request.OriginalName is { } original && IdsOf(original) is null)
        {
            return TestErrors.Create(ErrorCode.ModCollectionNotFound);
        }

        var target = request.OriginalName ?? request.Name;
        Collections.RemoveAll(collection => collection.Name == request.Name && collection.Name != target);
        var index = Collections.FindIndex(collection => collection.Name == target);
        if (index >= 0)
        {
            Collections[index] = (request.Name, request.ModIds);
        }
        else
        {
            Collections.Add((request.Name, request.ModIds));
        }

        return Result.Ok();
    }

    public Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        Deletes.Add(name);
        return Task.FromResult(Collections.RemoveAll(collection => collection.Name == name) > 0
            ? Result.Ok()
            : TestErrors.Create(ErrorCode.ModCollectionNotFound));
    }

    public Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        Applies.Add((installRoot, extraGameArgs, name));
        if (ApplyError(name) is { } error)
        {
            return Task.FromResult<Result<ApplyModCollectionResult>>(error);
        }

        if (IdsOf(name) is not { } ids)
        {
            return Task.FromResult<Result<ApplyModCollectionResult>>(TestErrors.Create(ErrorCode.ModCollectionNotFound));
        }

        var resolved = ModLoadOrder.Resolve(ids, InstalledMods);
        ActiveIds = [.. resolved.EnabledIds];
        return Task.FromResult<Result<ApplyModCollectionResult>>(resolved);
    }
}

public static class TestMods
{
    public static InstalledMod Mod(
        string id, string? name = null, ulong? workshopId = null, InstalledModSource source = InstalledModSource.SteamWorkshop,
        string[]? require = null, string[]? incompatible = null) =>
        new(id, name ?? id + " Name", workshopId, source, require ?? [], [], [], incompatible ?? []);
}
