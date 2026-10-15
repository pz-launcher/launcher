namespace PzLauncher.Core.Java;

/// <param name="HomePath">Absolute, normalized, links resolved.</param>
/// <param name="ExecutablePath"><c>bin/javaw.exe</c> on Windows, <c>bin/java</c> elsewhere.</param>
/// <param name="Version"><c>null</c> when the <c>release</c> file doesn't say.</param>
/// <param name="Vendor"><c>null</c> when the <c>release</c> file doesn't say.</param>
public sealed record JavaRuntime(
    string HomePath,
    string ExecutablePath,
    JavaVersion? Version,
    string? Vendor,
    JavaRuntimeSource Source);
