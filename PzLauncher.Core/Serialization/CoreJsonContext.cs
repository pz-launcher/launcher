using System.Text.Json.Serialization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Zabric;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Serialization;

/// <summary>Every DTO that crosses the Core boundary must be listed here.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(ErrorCode))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PzInstall))]
[JsonSerializable(typeof(SteamAppInfo))]
[JsonSerializable(typeof(InstallScanResult))]
[JsonSerializable(typeof(JavaRuntime))]
[JsonSerializable(typeof(JavaVersion))]
[JsonSerializable(typeof(JavaScanResult))]
[JsonSerializable(typeof(LaunchGameRequest))]
[JsonSerializable(typeof(GameLaunchOverrides))]
[JsonSerializable(typeof(GameLaunchDefaults))]
[JsonSerializable(typeof(GameSession))]
[JsonSerializable(typeof(ZombieBuddyChannel))]
[JsonSerializable(typeof(ZombieBuddyStatus))]
[JsonSerializable(typeof(ZombieBuddyProgress))]
[JsonSerializable(typeof(ZabricPreload))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
