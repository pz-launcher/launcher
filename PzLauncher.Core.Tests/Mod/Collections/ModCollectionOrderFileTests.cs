using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.Collections;

public sealed class ModCollectionOrderFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _cache;

    public ModCollectionOrderFileTests()
    {
        _cache = _temp.CreateDirectory("Zomboid");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_cache, "Lua", "pz_modlist_settings.pzlauncher.json");

    private Task<Dictionary<string, IReadOnlyList<string>>> ReadAsync() => ModCollectionOrderFile.ReadAsync(_cache, NullLogger.Instance, Ct);

    [Fact]
    public async Task WriteAsync_ThenRead_RoundTripsNamesAndOrder()
    {
        ResultAssert.Ok(await ModCollectionOrderFile.WriteAsync(
            _cache, new Dictionary<string, IReadOnlyList<string>> { ["my合集"] = ["B", "A"], ["other"] = [] }, Ct));

        var orders = await ReadAsync();

        Assert.Equal(["B", "A"], orders["my合集"]);
        Assert.Empty(orders["other"]);
        Assert.Contains("\"collections\"", await File.ReadAllTextAsync(FilePath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_Missing_IsEmpty() =>
        Assert.Empty(await ReadAsync());

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"collections":{"a":"not a list"}}""")]
    [InlineData("""{"other":1}""")]
    [InlineData("""{"collections":{"a":null}}""")]
    [InlineData("""{"collections":null}""")]
    public async Task ReadAsync_Invalid_IsIgnored(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, json, Ct);

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public void Reconcile_NoRememberedOrder_KeepsTheFileOrder() =>
        Assert.Equal(["C", "A", "B"], ModCollectionOrderFile.Reconcile(["C", "A", "B"], null));

    [Fact]
    public void Reconcile_ShuffledInGame_RestoresTheRememberedOrderAndAppendsNewIds() =>
        Assert.Equal(
            ["A", "B", "C", "New1", "New2"],
            ModCollectionOrderFile.Reconcile(["New1", "C", "B", "New2", "A"], ["A", "Removed", "B", "C"]));
}
