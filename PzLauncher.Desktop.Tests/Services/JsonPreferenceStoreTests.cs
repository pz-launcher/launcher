using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Tests.Services;

public sealed class JsonPreferenceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public static TheoryData<AppTheme> Themes => [.. Enum.GetValues<AppTheme>()];

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

        Assert.Equal(new UserPreferences(language, null), CreateStore().Load());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SaveThemeAsync_ThenLoad_RoundTrips(AppTheme theme)
    {
        await CreateStore().SaveThemeAsync(theme, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, theme), CreateStore().Load());
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
    public async Task SaveLanguageAsync_Twice_LastChoiceWins()
    {
        var store = CreateStore();

        await store.SaveLanguageAsync(AppLanguage.TraditionalChinese, TestContext.Current.CancellationToken);
        await store.SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);

        Assert.Equal(AppLanguage.English, store.Load().Language);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveOneField_FileHoldsTheOther_KeepsBoth(bool themeFirst)
    {
        var ct = TestContext.Current.CancellationToken;

        // Separate stores, as across two app runs.
        if (themeFirst)
        {
            await CreateStore().SaveThemeAsync(AppTheme.Dark, ct);
            await CreateStore().SaveLanguageAsync(AppLanguage.Russian, ct);
        }
        else
        {
            await CreateStore().SaveLanguageAsync(AppLanguage.Russian, ct);
            await CreateStore().SaveThemeAsync(AppTheme.Dark, ct);
        }

        Assert.Equal(new UserPreferences(AppLanguage.Russian, AppTheme.Dark), CreateStore().Load());
    }

    [Fact]
    public async Task SaveBothFields_Concurrently_KeepsBoth()
    {
        var store = CreateStore();
        store.Load();

        await Task.WhenAll(
            store.SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken),
            store.SaveLanguageAsync(AppLanguage.PortugueseBrazil, TestContext.Current.CancellationToken));

        Assert.Equal(new UserPreferences(AppLanguage.PortugueseBrazil, AppTheme.Dark), CreateStore().Load());
    }

    [Fact]
    public async Task SaveEitherField_TargetUnwritable_DoesNotThrow()
    {
        Directory.CreateDirectory(FilePath);

        await CreateStore().SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);
        await CreateStore().SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsNoChoices() => Assert.Equal(new UserPreferences(null, null), CreateStore().Load());

    [Fact]
    public void Load_LanguageOnlyFileFromEarlierVersion_LoadsLanguageWithoutTheme()
    {
        WriteFile("""{"language":"ru"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null), CreateStore().Load());
    }

    [Fact]
    public void Load_UnknownTheme_KeepsLanguageAndReturnsNoTheme()
    {
        WriteFile("""{"language":"ru","theme":"sepia"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null), CreateStore().Load());
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
    public void Load_CorruptOrUnknownContent_ReturnsNoChoices(string content)
    {
        WriteFile(content);

        Assert.Equal(new UserPreferences(null, null), CreateStore().Load());
    }
}
