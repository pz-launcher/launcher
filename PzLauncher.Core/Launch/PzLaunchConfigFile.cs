using PzLauncher.Core.Common;

namespace PzLauncher.Core.Launch;

internal static class PzLaunchConfigFile
{
    public static async Task<Result<string>> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return LaunchErrors.ConfigMissing(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LaunchErrors.ConfigMalformed(path, ex.Message);
        }
    }
}
