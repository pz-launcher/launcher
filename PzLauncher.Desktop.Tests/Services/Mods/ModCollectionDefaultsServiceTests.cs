using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class ModCollectionDefaultsServiceTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";

    private readonly FakePreferenceStore _store = new();

    private ModCollectionDefaultsService CreateService(StringComparison pathComparison = StringComparison.OrdinalIgnoreCase) =>
        new(
            _store,
            new UserPreferences(null, null, null) { DefaultModCollections = new Dictionary<string, string> { [GameRoot] = "Pack" } },
            new FakePlatformEnvironment { PathComparison = pathComparison });

    [Fact]
    public void Get_NothingSaved_ReturnsNull() => Assert.Null(CreateService().Get(OtherRoot));

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, "Pack")]
    [InlineData(StringComparison.Ordinal, null)]
    public void Get_PathInOtherCase_FollowsThePlatformComparison(StringComparison comparison, string? expected) =>
        Assert.Equal(expected, CreateService(comparison).Get(GameRoot.ToUpperInvariant()));

    [Fact]
    public void Save_MovesTheMarkAndWritesIt()
    {
        var service = CreateService();

        service.Save(GameRoot, "Other pack");

        Assert.Equal("Other pack", service.Get(GameRoot));
        Assert.Equal([(GameRoot, (string?)"Other pack")], _store.SavedDefaultModCollections);
    }

    [Fact]
    public void Save_PathInOtherCase_WritesUnderTheSavedSpelling()
    {
        var service = CreateService();

        service.Save(GameRoot.ToUpperInvariant(), "Other pack");

        Assert.Equal([(GameRoot, (string?)"Other pack")], _store.SavedDefaultModCollections);
    }

    [Fact]
    public void Clear_RemovesTheMarkAndWritesNull()
    {
        var service = CreateService();

        service.Clear(GameRoot.ToUpperInvariant());

        Assert.Null(service.Get(GameRoot));
        Assert.Equal([(GameRoot, (string?)null)], _store.SavedDefaultModCollections);
    }

    [Fact]
    public void Clear_NothingMarked_WritesNothing()
    {
        CreateService().Clear(OtherRoot);

        Assert.Empty(_store.SavedDefaultModCollections);
    }
}
