using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services;

public sealed class JsonLanguagePreferenceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // In a folder that doesn't exist yet, as on first launch.
    private string FilePath => Path.Combine(_temp.Path, "PzLauncher", "preferences.json");

    private JsonLanguagePreferenceStore CreateStore() => new(FilePath, NullLogger.Instance);

    public void Dispose() => _temp.Dispose();

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public async Task SaveAsync_ThenLoad_RoundTrips(AppLanguage language)
    {
        await CreateStore().SaveAsync(language, TestContext.Current.CancellationToken);

        Assert.Equal(language, CreateStore().Load());
    }

    [Fact]
    public async Task SaveAsync_WritesCultureName()
    {
        await CreateStore().SaveAsync(AppLanguage.SimplifiedChinese, TestContext.Current.CancellationToken);

        Assert.Equal("""{"language":"zh-Hans"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_Twice_LastChoiceWins()
    {
        var store = CreateStore();

        await store.SaveAsync(AppLanguage.TraditionalChinese, TestContext.Current.CancellationToken);
        await store.SaveAsync(AppLanguage.English, TestContext.Current.CancellationToken);

        Assert.Equal(AppLanguage.English, store.Load());
    }

    [Fact]
    public async Task SaveAsync_TargetUnwritable_DoesNotThrow()
    {
        Directory.CreateDirectory(FilePath);

        await CreateStore().SaveAsync(AppLanguage.English, TestContext.Current.CancellationToken);

        Assert.Null(CreateStore().Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull() => Assert.Null(CreateStore().Load());

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"language":5}""")]
    [InlineData("""{"language":"fr"}""")]
    [InlineData("""{"language":"SimplifiedChinese"}""")]
    public void Load_CorruptOrUnknownContent_ReturnsNull(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        Assert.Null(CreateStore().Load());
    }
}
