using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

public partial class StringsResourcesTests
{
    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void ResourceSet_EveryLanguage_HasEveryKeyWithTheSamePlaceholders(AppLanguage language)
    {
        var neutral = Read(CultureInfo.InvariantCulture);
        var localized = Read(CultureInfo.GetCultureInfo(language.CultureName()));

        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), localized.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, value) in neutral)
        {
            Assert.False(string.IsNullOrWhiteSpace(localized[key]), key);
            Assert.Equal(Placeholders(value), Placeholders(localized[key]));
        }
    }

    // tryParents: false, so a key missing from a satellite isn't hidden by the English fallback.
    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string ?? string.Empty);
    }

    private static string Placeholders(string text) =>
        string.Join(",", PlaceholderPattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal));

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
