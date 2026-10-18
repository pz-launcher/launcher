using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop;

public sealed class WorkshopManifestFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Library(string name, string? acf)
    {
        var library = _temp.CreateDirectory(name);
        if (acf is not null)
        {
            SteamLayoutBuilder.WriteRaw(WorkshopFiles.ManifestPath(library), acf);
        }

        return library;
    }

    private static string Acf(ulong id, string manifest, string? latest = null) =>
        $$"""
        "AppWorkshop"
        {
            "WorkshopItemsInstalled" { "{{id}}" { "size" "10" "timeupdated" "100" "manifest" "{{manifest}}" } }
            "WorkshopItemDetails" { "{{id}}" { "manifest" "{{manifest}}" "latest_manifest" "{{latest ?? manifest}}" } }
        }
        """;

    [Fact]
    public async Task ReadAsync_RealShape_ReadsInstalledItemsWithSteamsLatestManifest()
    {
        var library = Library("Lib", Fixtures.Read("Steam", "appworkshop_108600.acf"));

        var manifest = await WorkshopManifestFile.ReadAsync([library], NullLogger.Instance, Ct);

        Assert.Empty(manifest.Warnings);
        Assert.Equal([2409333430UL, 3811390574UL], manifest.Items.Keys.Order());
        Assert.Equal(
            new WorkshopManifestEntry(
                new WorkshopInstall(WorkshopLibraries.ItemFolder(library, "2409333430"), "2026051810389825471", DateTimeOffset.FromUnixTimeSeconds(1791483143), 18928379),
                "2026051810389825471"),
            manifest.Items[2409333430]);
        Assert.Equal(
            new WorkshopManifestEntry(
                new WorkshopInstall(WorkshopLibraries.ItemFolder(library, "3811390574"), "14459181659745603925", DateTimeOffset.FromUnixTimeSeconds(1792336500), 253378238),
                "987951682342026743"),
            manifest.Items[3811390574]);
    }

    [Fact]
    public async Task ReadAsync_ItemInTwoLibraries_FirstLibraryWins()
    {
        var first = Library("First", Acf(5, "111"));
        var second = Library("Second", Acf(5, "222"));
        var third = Library("Third", Acf(6, "444"));

        var manifest = await WorkshopManifestFile.ReadAsync([first, second, third], NullLogger.Instance, Ct);

        Assert.Equal("111", manifest.Items[5].Install.Manifest);
        Assert.Equal(WorkshopLibraries.ItemFolder(first, "5"), manifest.Items[5].Install.Folder);
        Assert.Equal("444", manifest.Items[6].Install.Manifest);
    }

    [Fact]
    public async Task ReadAsync_LibraryWithoutFile_HasNoItemsAndNoWarning()
    {
        var manifest = await WorkshopManifestFile.ReadAsync([Library("Empty", null)], NullLogger.Instance, Ct);

        Assert.Empty(manifest.Items);
        Assert.Empty(manifest.Warnings);
    }

    [Fact]
    public async Task ReadAsync_MalformedFile_WarnsAndKeepsOtherLibraries()
    {
        var bad = Library("Bad", "\"AppWorkshop\"\n{\n");
        var good = Library("Good", Acf(5, "111"));

        var manifest = await WorkshopManifestFile.ReadAsync([bad, good], NullLogger.Instance, Ct);

        var warning = Assert.Single(manifest.Warnings);
        Assert.Equal(ErrorCode.WorkshopManifestUnreadable, warning.Code);
        Assert.Equal(WorkshopFiles.ManifestPath(bad), warning.Details["path"]);
        Assert.Equal([5UL], manifest.Items.Keys);
    }

    [Fact]
    public async Task ReadAsync_UnreadableFile_Warns()
    {
        var library = _temp.CreateDirectory("Locked");
        Directory.CreateDirectory(WorkshopFiles.ManifestPath(library));

        var manifest = await WorkshopManifestFile.ReadAsync([library], NullLogger.Instance, Ct);

        Assert.Equal(ErrorCode.WorkshopManifestUnreadable, Assert.Single(manifest.Warnings).Code);
    }

    [Fact]
    public void Parse_MalformedEntries_AreSkippedAndTheRestKept()
    {
        var items = ResultAssert.Ok(WorkshopManifestFile.Parse(
            """
            "AppWorkshop"
            {
                "WorkshopItemsInstalled"
                {
                    "abc" { "size" "1" "timeupdated" "1" "manifest" "1" }
                    "2" { "size" "1" "timeupdated" "1" "manifest" "x" }
                    "3" { "size" "-1" "timeupdated" "1" "manifest" "1" }
                    "4" { "size" "1" "manifest" "1" }
                    "5" "flat"
                    "6" { "size" "1" "timeupdated" "1" "manifest" "18446744073709551615" }
                }
                "WorkshopItemDetails" { "6" { "latest_manifest" "bad" } }
            }
            """,
            "lib",
            "p",
            NullLogger.Instance));

        var (id, entry) = Assert.Single(items);
        Assert.Equal(6UL, id);
        Assert.Equal("18446744073709551615", entry.Install.Manifest);
        Assert.Null(entry.LatestManifest);
    }

    [Fact]
    public void Parse_NoAppWorkshopBlock_IsManifestUnreadable() =>
        ResultAssert.Failed(WorkshopManifestFile.Parse("\"Other\"\n{\n}\n", "lib", "p", NullLogger.Instance), ErrorCode.WorkshopManifestUnreadable);
}
