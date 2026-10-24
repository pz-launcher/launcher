using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Server.Config;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server.Config;

public sealed class ServerIniFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _path;

    public ServerIniFileTests() => _path = Path.Combine(_temp.Path, "pz.ini");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ServerIniFile> LoadAsync(byte[] content)
    {
        await File.WriteAllBytesAsync(_path, content, Ct);
        var read = await ServerIniFile.ReadAsync(_path, Ct);
        Assert.NotNull(read);
        return ResultAssert.Ok(read.Value);
    }

    private async Task<byte[]> SetAsync(byte[] content, string key, string value)
    {
        var ini = await LoadAsync(content);
        ini.Set(key, value);
        ResultAssert.Ok(await ini.WriteAsync(Ct));
        return await File.ReadAllBytesAsync(_path, Ct);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_IsNull() =>
        Assert.Null(await ServerIniFile.ReadAsync(Path.Combine(_temp.Path, "missing.ini"), Ct));

    [Fact]
    public async Task Get_RealFile_ReadsValues()
    {
        var ini = await LoadAsync(await File.ReadAllBytesAsync(Fixtures.Path("Server", "server.ini"), Ct));

        Assert.Equal("27115", ini.Get("RCONPort"));
        Assert.Equal("spike-rcon-7f3a", ini.Get("RCONPassword"));
        Assert.Equal("", ini.Get("Password"));
        Assert.Null(ini.Get("NoSuchKey"));
    }

    [Fact]
    public async Task Get_DuplicateKeyAndComments_ReturnsTheLastValueTrimmed()
    {
        var ini = await LoadAsync("# RCONPort=1\nRCONPort=2\n  RCONPort = 3 \r\nrconport=4\n"u8.ToArray());

        Assert.Equal("3", ini.Get("RCONPort"));
    }

    [Fact]
    public async Task Set_RealFile_ChangesOnlyThatLine()
    {
        var original = await File.ReadAllBytesAsync(Fixtures.Path("Server", "server.ini"), Ct);

        var updated = await SetAsync(original, "RCONPassword", "new");

        var expected = Encoding.UTF8.GetString(original).Replace("RCONPassword=spike-rcon-7f3a", "RCONPassword=new", StringComparison.Ordinal);
        Assert.Equal(expected, Encoding.UTF8.GetString(updated));
    }

    [Fact]
    public async Task ReadAsync_NotUtf8_FailsFileAccessFailed()
    {
        await File.WriteAllBytesAsync(_path, [.. "RCONPort=1\r\nPublicDescription="u8, 0xFF, 0xFE, .. "\r\n"u8], Ct);

        var read = await ServerIniFile.ReadAsync(_path, Ct);

        Assert.NotNull(read);
        ResultAssert.Failed(read.Value, ErrorCode.ServerFileAccessFailed);
    }

    [Fact]
    public async Task Set_WithBom_KeepsTheBom()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];

        var updated = await SetAsync([.. bom, .. "RCONPort=1\r\nA=1\r\n"u8], "RCONPort", "2");

        Assert.Equal([.. bom, .. "RCONPort=2\r\nA=1\r\n"u8], updated);
    }

    [Fact]
    public async Task WriteAsync_Changed_KeepsNoBackup()
    {
        await SetAsync("RCONPort=1\n"u8.ToArray(), "RCONPort", "2");

        Assert.Equal(["pz.ini"], Directory.EnumerateFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Set_DuplicateKey_ReplacesEveryLine()
    {
        var updated = await SetAsync("RCONPort=1\r\nA=1\r\nRCONPort=2\r\n"u8.ToArray(), "RCONPort", "3");

        Assert.Equal("RCONPort=3\r\nA=1\r\nRCONPort=3\r\n", Encoding.UTF8.GetString(updated));
    }

    [Theory]
    [InlineData("A=1\r\nB=2", "A=1\r\nB=2\r\nRCONPort=7\r\n")]
    [InlineData("A=1\n", "A=1\nRCONPort=7\n")]
    [InlineData("", "RCONPort=7\n")]
    public async Task Set_MissingKey_AppendsInTheFilesLineEnding(string content, string expected)
    {
        var updated = await SetAsync(Encoding.UTF8.GetBytes(content), "RCONPort", "7");

        Assert.Equal(expected, Encoding.UTF8.GetString(updated));
    }

    [Fact]
    public void Create_WritesOneLinePerEntry() =>
        Assert.Equal(
            "RCONPort=27015\nRCONPassword=x\n",
            Encoding.UTF8.GetString(ServerIniFile.Create([new("RCONPort", "27015"), new("RCONPassword", "x")])));
}
