using System.Globalization;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Every test class that constructs a <see cref="LocalizationService"/> or asserts localized text belongs here: the static <see cref="Strings.Culture"/> is shared.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StringsCultureCollection
{
    public const string Name = "Strings.Culture";
}

/// <summary>Starts each test in English and restores <see cref="Strings.Culture"/> afterwards.</summary>
public abstract class StringsCultureTest : IDisposable
{
    private readonly CultureInfo? _saved;

    // Runs after derived field initializers, so those must not touch Localization.
    protected StringsCultureTest()
    {
        _saved = Strings.Culture;
        Localization = new LocalizationService(AppLanguage.English);
    }

    protected LocalizationService Localization { get; }

    public void Dispose()
    {
        Strings.Culture = _saved;
        GC.SuppressFinalize(this);
    }
}

public static class TestLanguages
{
    public static TheoryData<AppLanguage> All => [.. Enum.GetValues<AppLanguage>()];
}
