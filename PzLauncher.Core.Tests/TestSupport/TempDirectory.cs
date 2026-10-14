namespace PzLauncher.Core.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PzLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(params string[] parts)
    {
        var dir = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS cleans the temp folder eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
