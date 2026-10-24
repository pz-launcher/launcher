using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzLauncher.Core.Launch.Config;

// Raw shape of ProjectZomboid64.json: everything nullable, validated in PzLaunchConfig.Parse.
internal sealed class PzLaunchConfigJson
{
    public string? MainClass { get; set; }

    public List<string?>? Classpath { get; set; }

    public List<string?>? VmArgs { get; set; }

    public Dictionary<string, PzLaunchOsEntryJson?>? Windows { get; set; }
}

internal sealed class PzLaunchOsEntryJson
{
    public List<string?>? VmArgs { get; set; }
}

// Separate from CoreJsonContext: this reads an external file leniently, it isn't the Core wire contract.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PzLaunchConfigJson))]
internal sealed partial class PzLaunchConfigJsonContext : JsonSerializerContext;
