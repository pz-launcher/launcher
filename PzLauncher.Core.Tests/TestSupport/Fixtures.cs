namespace PzLauncher.Core.Tests.TestSupport;

public static class Fixtures
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(Path(parts));
}
