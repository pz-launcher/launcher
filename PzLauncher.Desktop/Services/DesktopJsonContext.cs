using System.Text.Json.Serialization;

namespace PzLauncher.Desktop.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferencesFile))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;

/// <summary>Stable strings, not enum names (<see cref="Localization.AppLanguages.CultureName"/>, <see cref="Themes.AppThemes.PreferenceName"/>), so renaming an enum keeps saved files valid.</summary>
internal sealed record PreferencesFile(string? Language, string? Theme, WindowFile? Window);

/// <summary>Nullable members, so a missing one is told apart from a zero.</summary>
internal sealed record WindowFile(int? X, int? Y, double? Width, double? Height, bool? Maximized);
