using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Tests.Services;

public sealed class JsonPreferenceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static readonly WindowPlacement Placement = new(-1910, 30, 1000.5, 640, IsMaximized: true);

    public static TheoryData<AppTheme> Themes => [.. Enum.GetValues<AppTheme>()];

    public static TheoryData<WindowPlacement> Placements =>
    [
        Placement,
        new WindowPlacement(120, 80, 820, 520, IsMaximized: false),
    ];

    // In a folder that doesn't exist yet, as on first launch.
    private string FilePath => Path.Combine(_temp.Path, "PzLauncher", "preferences.json");

    private JsonPreferenceStore CreateStore() => new(FilePath, NullLogger.Instance);

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public async Task SaveLanguageAsync_ThenLoad_RoundTrips(AppLanguage language)
    {
        await CreateStore().SaveLanguageAsync(language, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(language, null, null), CreateStore().Load());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SaveThemeAsync_ThenLoad_RoundTrips(AppTheme theme)
    {
        await CreateStore().SaveThemeAsync(theme, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, theme, null), CreateStore().Load());
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task SaveWindowPlacementAsync_ThenLoad_RoundTrips(WindowPlacement placement)
    {
        await CreateStore().SaveWindowPlacementAsync(placement, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, placement), CreateStore().Load());
    }

    [Fact]
    public async Task SaveLanguageAsync_WritesCultureName()
    {
        await CreateStore().SaveLanguageAsync(AppLanguage.SimplifiedChinese, TestContext.Current.CancellationToken);

        Assert.Equal("""{"language":"zh-Hans"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.Dark, "dark")]
    public async Task SaveThemeAsync_WritesStableName(AppTheme theme, string expected)
    {
        await CreateStore().SaveThemeAsync(theme, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"theme":"{{expected}}"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveWindowPlacementAsync_WritesWindowObject()
    {
        await CreateStore().SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"window":{"x":-1910,"y":30,"width":1000.5,"height":640,"maximized":true}}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveLanguageAsync_Twice_LastChoiceWins()
    {
        var store = CreateStore();

        await store.SaveLanguageAsync(AppLanguage.TraditionalChinese, TestContext.Current.CancellationToken);
        await store.SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);

        Assert.Equal(AppLanguage.English, store.Load().Language);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SaveOneField_FileHoldsTheOthers_KeepsAll(int savedLast)
    {
        var ct = TestContext.Current.CancellationToken;
        Func<Task>[] saves =
        [
            () => CreateStore().SaveLanguageAsync(AppLanguage.Russian, ct),
            () => CreateStore().SaveThemeAsync(AppTheme.Dark, ct),
            () => CreateStore().SaveWindowPlacementAsync(Placement, ct),
        ];

        // Separate stores, as across app runs.
        foreach (var save in saves.Where((_, i) => i != savedLast).Append(saves[savedLast]))
        {
            await save();
        }

        Assert.Equal(new UserPreferences(AppLanguage.Russian, AppTheme.Dark, Placement), CreateStore().Load());
    }

    [Fact]
    public async Task SaveAllFields_Concurrently_KeepsAll()
    {
        var store = CreateStore();
        store.Load();

        await Task.WhenAll(
            store.SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken),
            store.SaveLanguageAsync(AppLanguage.PortugueseBrazil, TestContext.Current.CancellationToken),
            store.SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken));

        Assert.Equal(new UserPreferences(AppLanguage.PortugueseBrazil, AppTheme.Dark, Placement), CreateStore().Load());
    }

    [Fact]
    public async Task SaveAnyField_TargetUnwritable_DoesNotThrow()
    {
        Directory.CreateDirectory(FilePath);

        await CreateStore().SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);
        await CreateStore().SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken);
        await CreateStore().SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsNoChoices() => Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());

    [Fact]
    public void Load_LanguageOnlyFileFromEarlierVersion_LoadsLanguageWithoutTheme()
    {
        WriteFile("""{"language":"ru"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_UnknownTheme_KeepsLanguageAndReturnsNoTheme()
    {
        WriteFile("""{"language":"ru","theme":"sepia"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null, null), CreateStore().Load());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"y":30,"width":1000,"height":640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":640}""")]
    [InlineData("""{"x":10,"y":30,"width":0,"height":640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":-640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":null,"maximized":false}""")]
    public void Load_InvalidWindowPlacement_KeepsOtherFieldsAndReturnsNoPlacement(string window)
    {
        WriteFile($$"""{"language":"ru","theme":"dark","window":{{window}}}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, AppTheme.Dark, null), CreateStore().Load());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"language":5}""")]
    [InlineData("""{"language":"fr"}""")]
    [InlineData("""{"language":"es"}""")]
    [InlineData("""{"language":"pt"}""")]
    [InlineData("""{"language":"SimplifiedChinese"}""")]
    [InlineData("""{"theme":1}""")]
    [InlineData("""{"theme":"Default"}""")]
    [InlineData("""{"window":5}""")]
    [InlineData("""{"window":{"x":"left"}}""")]
    [InlineData("""{"window":{"x":10,"y":30,"width":NaN,"height":640,"maximized":false}}""")]
    public void Load_CorruptOrUnknownContent_ReturnsNoChoices(string content)
    {
        WriteFile(content);

        Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());
    }
}
