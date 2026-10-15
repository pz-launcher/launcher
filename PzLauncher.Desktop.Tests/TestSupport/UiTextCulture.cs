using PzLauncher.Desktop.Lang;
using System.Globalization;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Every test class that constructs a <see cref="LocalizationService"/> or asserts localized text belongs here: the static <see cref="UiText.Culture"/> is shared.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiTextCultureCollection
{
    public const string Name = "UiText.Culture";
}

/// <summary>Starts each test in English and restores <see cref="UiText.Culture"/> afterwards.</summary>
public abstract class UiTextCultureTest : IDisposable
{
    private readonly CultureInfo? _saved;

    // Runs after derived field initializers, so those must not touch Localization.
    protected UiTextCultureTest()
    {
        _saved = UiText.Culture;
        Localization = new LocalizationService(AppLanguage.English);
    }

    protected LocalizationService Localization { get; }

    public void Dispose()
    {
        UiText.Culture = _saved;
        GC.SuppressFinalize(this);
    }
}

public static class TestLanguages
{
    public static TheoryData<AppLanguage> All => [.. Enum.GetValues<AppLanguage>()];
}
