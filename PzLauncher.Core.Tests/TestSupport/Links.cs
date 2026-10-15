namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Creates symbolic links, skipping the test where the OS doesn't allow it (Windows without Developer Mode).</summary>
public static class Links
{
    public static void Directory(string link, string target) =>
        Create(link, () => System.IO.Directory.CreateSymbolicLink(link, target));

    public static void File(string link, string target) =>
        Create(link, () => System.IO.File.CreateSymbolicLink(link, target));

    private static void Create(string link, Action create)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Symbolic links can't be created here: {ex.Message}");
        }
    }
}
