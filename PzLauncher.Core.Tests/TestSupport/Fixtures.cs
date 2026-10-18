namespace PzLauncher.Core.Tests.TestSupport;

public static class Fixtures
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(Path(parts));

    // Git may check a fixture out with CRLF; tests that edit it by exact lines need the committed LF.
    public static string ReadLf(params string[] parts) => Read(parts).ReplaceLineEndings("\n");
}
