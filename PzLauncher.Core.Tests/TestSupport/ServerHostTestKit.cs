using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.Installs;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>A dedicated server install with a cache and a state folder in a temp directory, fake processes and fake RCON.</summary>
internal sealed class ServerHostTestKit : IDisposable
{
    public ServerHostTestKit(PlatformKind os = PlatformKind.Windows)
    {
        Platform = new FakePlatformEnvironment
        {
            Os = os,
            HomeDirectory = Temp.CreateDirectory("home"),
            PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
        };
        Installs = new GameInstallBuilder(Temp.Path, os);
        Site = new ServerSite(Installs.CreateServer("Server"), Path.Combine(Temp.Path, "cache"), Path.Combine(Temp.Path, "state"));
    }

    public TempDirectory Temp { get; } = new();

    public FakePlatformEnvironment Platform { get; }

    public GameInstallBuilder Installs { get; }

    public FakeProcessRunner Processes { get; } = new();

    public FakeRconClient Rcon { get; } = new();

    public ServerSite Site { get; set; }

    public string RunningFile => Path.Combine(Site.StateDir, "server", "running.json");

    public RunningServer Running =>
        JsonSerializer.Deserialize(File.ReadAllBytes(RunningFile), RunningServerJsonContext.Default.RunningServer)
        ?? throw new InvalidOperationException("The running file holds null.");

    /// <summary>Every delay passes at once unless <paramref name="time"/> says otherwise.</summary>
    public ServerHostService Service(TimeProvider? time = null, TimeSpan? stopTimeout = null) =>
        new(
            InstallDetectionServiceScanTests.CreateService(Platform),
            Platform,
            Processes,
            Rcon,
            time ?? new SteppingTimeProvider(),
            NullLogger<ServerHostService>.Instance)
        {
            StopTimeout = stopTimeout ?? TimeSpan.FromMinutes(3),
        };

    public string IniPath(string name) => Path.Combine(Site.CacheDir, "Server", name + ".ini");

    public string WriteIni(string name, string content = "RCONPort=27015\nRCONPassword=pw\n")
    {
        var path = IniPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void CreateDatabase(string name)
    {
        var db = Directory.CreateDirectory(Path.Combine(Site.CacheDir, "db"));
        File.WriteAllBytes(Path.Combine(db.FullName, name + ".db"), []);
    }

    /// <summary>An existing server: ini with RCON settings and a database, so no admin password is needed.</summary>
    public void AddServer(string name)
    {
        WriteIni(name);
        CreateDatabase(name);
    }

    public string ConsolePath => Path.Combine(Site.CacheDir, "server-console.txt");

    public void WriteConsole(IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Site.CacheDir);
        File.WriteAllLines(ConsolePath, lines);
    }

    /// <summary>The process <see cref="FakeProcessRunner.StartDetached"/> started last exits.</summary>
    public void ExitServer() => Processes.Alive.Remove(Running.ProcessId);

    public void Dispose() => Temp.Dispose();
}
