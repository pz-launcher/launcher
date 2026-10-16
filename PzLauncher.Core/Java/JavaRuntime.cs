namespace PzLauncher.Core.Java;

/// <param name="HomePath">Absolute, normalized, links resolved.</param>
/// <param name="ExecutablePath"><c>bin/javaw.exe</c> on Windows, <c>bin/java</c> elsewhere.</param>
/// <param name="Version"><c>null</c> when the <c>release</c> file doesn't say.</param>
/// <param name="Vendor"><c>null</c> when the <c>release</c> file doesn't say.</param>
/// <param name="HasGraalCompiler">The Graal JIT is the default compiler (GraalVM), per the <c>release</c> file.</param>
/// <param name="HasInstrumentLibrary">The <c>java.instrument</c> native library exists, so <c>-javaagent</c> can load.</param>
public sealed record JavaRuntime(
    string HomePath,
    string ExecutablePath,
    JavaVersion? Version,
    string? Vendor,
    JavaRuntimeSource Source,
    bool HasGraalCompiler,
    bool HasInstrumentLibrary);
