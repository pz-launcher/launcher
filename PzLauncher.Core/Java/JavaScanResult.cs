using PzLauncher.Core.Common;

namespace PzLauncher.Core.Java;

public sealed record JavaScanResult(
    IReadOnlyList<JavaRuntime> Runtimes,
    IReadOnlyList<Error> Warnings);
