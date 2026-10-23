using System.ComponentModel;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server;

public sealed class ServerHostServiceStartTests : IDisposable
{
    private readonly ServerHostTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<ServerStatus>> StartAsync(string name = "pz", string? adminPassword = null, int? maxMemoryMb = null) =>
        _kit.Service().StartAsync(new StartServerRequest(_kit.Site, name, adminPassword, maxMemoryMb), Ct);

    [Fact]
    public async Task ListServersAsync_NoServerFolder_IsEmpty() =>
        Assert.Empty(ResultAssert.Ok(await _kit.Service().ListServersAsync(_kit.Site, Ct)));

    [Fact]
    public async Task ListServersAsync_IniFiles_ListsThemWithWorldAndDatabase()
    {
        _kit.WriteIni("b");
        _kit.WriteIni("A");
        _kit.CreateDatabase("A");
        Directory.CreateDirectory(Path.Combine(_kit.Site.CacheDir, "Saves", "Multiplayer", "b"));
        await File.WriteAllTextAsync(Path.Combine(_kit.Site.CacheDir, "Server", "A_SandboxVars.lua"), "", Ct);
        await File.WriteAllTextAsync(Path.Combine(_kit.Site.CacheDir, "Server", "A.ini.pzlauncher.bak"), "", Ct);

        var servers = ResultAssert.Ok(await _kit.Service().ListServersAsync(_kit.Site, Ct));

        Assert.Equal([new NamedServer("A", HasWorld: false, HasDatabase: true), new NamedServer("b", HasWorld: true, HasDatabase: false)], servers);
    }

    [Fact]
    public async Task CreateServerAsync_ValidName_WritesAMinimalIniWithRcon()
    {
        var created = ResultAssert.Ok(await _kit.Service().CreateServerAsync(_kit.Site, "my_server-1", Ct));

        Assert.Equal(new NamedServer("my_server-1", false, false), created);
        var lines = await File.ReadAllLinesAsync(_kit.IniPath("my_server-1"), Ct);
        Assert.Equal(2, lines.Length);
        Assert.Equal("RCONPort=27015", lines[0]);
        Assert.Matches("^RCONPassword=[A-Za-z0-9_-]{24}$", lines[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("my server")]
    [InlineData("../pz")]
    [InlineData("服务器")]
    [InlineData("a.b")]
    public async Task CreateServerAsync_InvalidName_FailsNameInvalid(string name) =>
        ResultAssert.Failed(await _kit.Service().CreateServerAsync(_kit.Site, name, Ct), ErrorCode.ServerNameInvalid);

    [Fact]
    public async Task CreateServerAsync_TooLongName_FailsNameInvalid() =>
        ResultAssert.Failed(await _kit.Service().CreateServerAsync(_kit.Site, new string('a', 65), Ct), ErrorCode.ServerNameInvalid);

    [Fact]
    public async Task CreateServerAsync_NameExistsInOtherCase_FailsAlreadyExists()
    {
        _kit.WriteIni("PZ", "keep");

        ResultAssert.Failed(await _kit.Service().CreateServerAsync(_kit.Site, "pz", Ct), ErrorCode.ServerAlreadyExists);
        Assert.Equal("keep", await File.ReadAllTextAsync(_kit.IniPath("PZ"), Ct));
    }

    [Fact]
    public async Task StartAsync_FirstStart_RunsTheBundledJavaWithServerArgs()
    {
        _kit.WriteIni("pz");

        var status = ResultAssert.Ok(await StartAsync(adminPassword: "secret"));

        var request = _kit.Processes.LastRequest;
        Assert.Equal(_kit.Installs.ServerJava(_kit.Site.InstallRoot), request.FileName);
        Assert.Equal(_kit.Site.InstallRoot, request.WorkingDirectory);
        Assert.Empty(request.Environment);
        Assert.Equal(
            [
                "-Djava.awt.headless=true", "-Xmx8g", "-Dzomboid.steam=1", "-Dzomboid.znetlog=1", "-Djava.library.path=natives/",
                "-XX:-CreateCoredumpOnCrash", "-XX:-OmitStackTraceInFastThrow", "-XX:+UseZGC", "-Drconlo=1",
                "-cp", "java/.;java/projectzomboid.jar", "zombie.network.GameServer",
                "-servername", "pz", "-cachedir=" + _kit.Site.CacheDir, "-adminpassword", "secret",
            ],
            request.Arguments);
        Assert.Equal(new ServerStatus(ServerState.Starting, "pz", 4001, _kit.Processes.DetachedStartTime, null), status);
    }

    [Fact]
    public async Task StartAsync_ExistingDatabaseWithoutPassword_PassesNoAdminPassword()
    {
        _kit.AddServer("pz");

        ResultAssert.Ok(await StartAsync(adminPassword: ""));

        Assert.DoesNotContain("-adminpassword", _kit.Processes.LastRequest.Arguments);
    }

    [Fact]
    public async Task StartAsync_NoDatabaseNoPassword_FailsAdminPasswordRequired()
    {
        _kit.WriteIni("pz");

        ResultAssert.Failed(await StartAsync(), ErrorCode.ServerAdminPasswordRequired);
        Assert.Empty(_kit.Processes.Requests);
    }

    [Fact]
    public async Task StartAsync_MemoryOverride_ReplacesTheJsonHeap()
    {
        _kit.AddServer("pz");

        ResultAssert.Ok(await StartAsync(maxMemoryMb: 4096));

        var args = _kit.Processes.LastRequest.Arguments;
        Assert.Contains("-Xmx4096m", args);
        Assert.DoesNotContain("-Xmx8g", args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StartAsync_NonPositiveMemory_FailsInvalidMemory(int maxMemoryMb)
    {
        _kit.AddServer("pz");

        ResultAssert.Failed(await StartAsync(maxMemoryMb: maxMemoryMb), ErrorCode.LaunchInvalidMemory);
    }

    [Fact]
    public async Task StartAsync_JsonSetsRconLoopback_KeepsItsOwn()
    {
        var json = GameInstallBuilder.ServerJson.Replace("\"-Xmx8g\",", "\"-Xmx8g\", \"-Drconlo=yes\",", StringComparison.Ordinal);
        _kit.Site = _kit.Site with { InstallRoot = _kit.Installs.CreateServer("Other", json) };
        _kit.AddServer("pz");

        ResultAssert.Ok(await StartAsync());

        Assert.Equal(["-Drconlo=yes"], _kit.Processes.LastRequest.Arguments.Where(arg => arg.StartsWith("-Drconlo", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StartAsync_DefaultCacheDir_PassesNoCacheDir()
    {
        _kit.Site = _kit.Site with { CacheDir = Path.Combine(_kit.Platform.HomeDirectory, "Zomboid") };
        _kit.AddServer("pz");

        ResultAssert.Ok(await StartAsync());

        Assert.DoesNotContain(_kit.Processes.LastRequest.Arguments, arg => arg.StartsWith("-cachedir", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_Linux_RunsJavaWithTheServerScriptEnvironment()
    {
        using var kit = new ServerHostTestKit(PlatformKind.Linux);
        kit.Platform.EnvironmentVariables["LD_LIBRARY_PATH"] = "/opt/lib";
        kit.Platform.EnvironmentVariables["LD_PRELOAD"] = "libextra.so";
        kit.AddServer("pz");

        ResultAssert.Ok(await kit.Service().StartAsync(new StartServerRequest(kit.Site, "pz", null, null), Ct));

        var request = kit.Processes.LastRequest;
        var root = kit.Site.InstallRoot;
        Assert.Equal(Path.Combine(root, "jre64", "bin", "java"), request.FileName);
        Assert.Contains("java/.:java/projectzomboid.jar", request.Arguments);
        Assert.DoesNotContain("-XX:+UseZGC", request.Arguments);
        Assert.Equal(2, request.Environment.Count);
        Assert.Equal(
            string.Join(':', Path.Combine(root, "linux64"), Path.Combine(root, "natives"), root, Path.Combine(root, "jre64", "lib"), "/opt/lib"),
            request.Environment["LD_LIBRARY_PATH"]);
        Assert.Equal("libextra.so:libjsig.so", request.Environment["LD_PRELOAD"]);
    }

    [Fact]
    public async Task StartAsync_MacOS_FailsPlatformUnsupported()
    {
        using var kit = new ServerHostTestKit(PlatformKind.MacOS);
        kit.AddServer("pz");

        ResultAssert.Failed(await kit.Service().StartAsync(new StartServerRequest(kit.Site, "pz", null, null), Ct), ErrorCode.LaunchPlatformUnsupported);
    }

    [Fact]
    public async Task StartAsync_IniWithoutRconSettings_AddsThemAndKeepsOtherLines()
    {
        const string ini = "# Comment\r\nPublic=true\r\nRCONPassword=\r\nUDPPort=16262";
        _kit.WriteIni("pz", ini);
        _kit.CreateDatabase("pz");

        ResultAssert.Ok(await StartAsync());

        var written = await File.ReadAllTextAsync(_kit.IniPath("pz"), Ct);
        var password = _kit.Running.RconPassword;
        Assert.Matches("^[A-Za-z0-9_-]{24}$", password);
        Assert.Equal($"# Comment\r\nPublic=true\r\nRCONPassword={password}\r\nUDPPort=16262\r\nRCONPort=27015\r\n", written);
        Assert.Equal(27015, _kit.Running.RconPort);
    }

    [Fact]
    public async Task StartAsync_IniWithRconSettings_LeavesTheFileAlone()
    {
        var real = await File.ReadAllBytesAsync(Fixtures.Path("Server", "server.ini"), Ct);
        _kit.WriteIni("pz");
        await File.WriteAllBytesAsync(_kit.IniPath("pz"), real, Ct);
        _kit.CreateDatabase("pz");

        ResultAssert.Ok(await StartAsync());

        Assert.Equal(real, await File.ReadAllBytesAsync(_kit.IniPath("pz"), Ct));
        Assert.Equal((27115, "spike-rcon-7f3a"), (_kit.Running.RconPort, _kit.Running.RconPassword));
    }

    [Fact]
    public async Task StartAsync_Started_RecordsTheRunningServer()
    {
        _kit.AddServer("pz");

        ResultAssert.Ok(await StartAsync());

        Assert.Equal(
            new RunningServer(4001, _kit.Processes.DetachedStartTime, "pz", _kit.Site.InstallRoot, _kit.Site.CacheDir, 27015, "pw", false, null),
            _kit.Running);
    }

    [Fact]
    public async Task StartAsync_GameInstall_FailsNotAServerInstall()
    {
        _kit.Site = _kit.Site with { InstallRoot = _kit.Installs.CreateFlat("Game", GameInstallBuilder.WindowsGameJson) };
        _kit.AddServer("pz");

        ResultAssert.Failed(await StartAsync(), ErrorCode.ServerNotAServerInstall);
    }

    [Fact]
    public async Task StartAsync_NoIni_FailsNotFound() =>
        ResultAssert.Failed(await StartAsync(), ErrorCode.ServerNotFound);

    [Theory]
    [InlineData("")]
    [InlineData("../pz")]
    [InlineData("a/b")]
    public async Task StartAsync_NotAFileName_FailsNameInvalid(string name) =>
        ResultAssert.Failed(await StartAsync(name), ErrorCode.ServerNameInvalid);

    [Fact]
    public async Task StartAsync_ServerJavaMissing_FailsJavaMissing()
    {
        _kit.AddServer("pz");
        File.Delete(_kit.Installs.ServerJava(_kit.Site.InstallRoot));

        ResultAssert.Failed(await StartAsync(), ErrorCode.LaunchJavaMissing);
    }

    [Fact]
    public async Task StartAsync_AnotherServerRuns_FailsAlreadyRunning()
    {
        _kit.AddServer("a");
        _kit.AddServer("b");
        var service = _kit.Service();
        ResultAssert.Ok(await service.StartAsync(new StartServerRequest(_kit.Site, "a", null, null), Ct));

        var error = ResultAssert.Failed(await service.StartAsync(new StartServerRequest(_kit.Site, "b", null, null), Ct), ErrorCode.ServerAlreadyRunning);

        Assert.Equal("a", error.Details["name"]);
        Assert.Single(_kit.Processes.Requests);
    }

    [Fact]
    public async Task StartAsync_ProcessCantStart_FailsStartFailedWithoutRecord()
    {
        _kit.AddServer("pz");
        _kit.Processes.StartException = new Win32Exception(2, "not found");

        ResultAssert.Failed(await StartAsync(), ErrorCode.ServerStartFailed);
        Assert.False(File.Exists(_kit.RunningFile));
    }

    [Fact]
    public async Task StartAsync_RunningFileUnwritable_KillsTheServerAndFails()
    {
        _kit.AddServer("pz");
        Directory.CreateDirectory(_kit.RunningFile);

        ResultAssert.Failed(await StartAsync(), ErrorCode.ServerFileAccessFailed);
        Assert.Equal([4001], _kit.Processes.Killed);
    }
}
