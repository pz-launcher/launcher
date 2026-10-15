using System.Text.Json.Serialization;

namespace PzLauncher.Desktop.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LanguagePreference))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;

/// <summary><see cref="Language"/> is a culture name, not the enum name, so renaming the enum keeps saved files valid.</summary>
internal sealed record LanguagePreference(string? Language);
