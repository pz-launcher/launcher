using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzLauncher.Desktop.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferencesFile))]
[JsonSerializable(typeof(string))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;

/// <summary>Stable strings, not enum names (<see cref="Localization.AppLanguages.CultureName"/>, <see cref="Themes.AppThemes.PreferenceName"/>), so renaming an enum keeps saved files valid.</summary>
/// <param name="JavaRuntimes">Elements, not strings, so one non-string entry doesn't make the whole file unreadable.</param>
internal sealed record PreferencesFile(string? Language, string? Theme, WindowFile? Window, IReadOnlyList<JsonElement>? JavaRuntimes);

/// <summary>Nullable members, so a missing one is told apart from a zero.</summary>
internal sealed record WindowFile(int? X, int? Y, double? Width, double? Height, bool? Maximized);
