using PzLauncher.Core.Common;

namespace PzLauncher.Core.Launch.Config;

internal static class PzLaunchConfigFile
{
    public static async Task<Result<string>> ReadAsync(string path, CancellationToken ct) =>
        await ReadFile.TextIfExistsAsync(path, reason => LaunchErrors.ConfigMalformed(path, reason), ct).ConfigureAwait(false)
            ?? LaunchErrors.ConfigMissing(path);
}
