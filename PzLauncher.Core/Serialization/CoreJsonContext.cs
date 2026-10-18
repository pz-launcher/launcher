using System.Text.Json.Serialization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Serialization;

/// <summary>Every DTO that crosses the Core boundary must be listed here.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(IReadOnlyList<Error>))]
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
[JsonSerializable(typeof(ZombieBuddyPreload))]
[JsonSerializable(typeof(InstalledMod))]
[JsonSerializable(typeof(InstalledModSource))]
[JsonSerializable(typeof(ModCollection))]
[JsonSerializable(typeof(ModCollectionState))]
[JsonSerializable(typeof(SaveModCollectionRequest))]
[JsonSerializable(typeof(ApplyModCollectionResult))]
[JsonSerializable(typeof(ModConflict))]
[JsonSerializable(typeof(SharedModCollection))]
[JsonSerializable(typeof(SemVersion))]
[JsonSerializable(typeof(UpdateSource))]
[JsonSerializable(typeof(ReleaseInfo))]
[JsonSerializable(typeof(UpdateCheck))]
[JsonSerializable(typeof(StagedUpdate))]
[JsonSerializable(typeof(DownloadProgress))]
[JsonSerializable(typeof(ProxyMode))]
[JsonSerializable(typeof(ManualProxy))]
[JsonSerializable(typeof(ProxySettings))]
[JsonSerializable(typeof(ConnectionTestResult))]
[JsonSerializable(typeof(IReadOnlyList<ConnectionTestResult>))]
[JsonSerializable(typeof(SteamAccount))]
[JsonSerializable(typeof(SteamLaunchHookState))]
[JsonSerializable(typeof(SteamLaunchHookStatus))]
[JsonSerializable(typeof(SteamLaunchHookApplied))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
