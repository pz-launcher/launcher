using System.Text;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server;

public sealed class ServerIniFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ServerIniFile> LoadAsync(byte[] content)
    {
        var path = Path.Combine(_temp.Path, "pz.ini");
        await File.WriteAllBytesAsync(path, content, Ct);
        var read = await ServerIniFile.ReadAsync(path, Ct);
        Assert.NotNull(read);
        return ResultAssert.Ok(read.Value);
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

        var updated = (await LoadAsync(original)).Set("RCONPassword", "new");

        var expected = Encoding.UTF8.GetString(original).Replace("RCONPassword=spike-rcon-7f3a", "RCONPassword=new", StringComparison.Ordinal);
        Assert.Equal(expected, Encoding.UTF8.GetString(updated.Content));
    }

    [Fact]
    public async Task Set_OtherLinesNotUtf8_KeepsTheirBytes()
    {
        byte[] original = [.. "Name="u8, 0xFF, 0xFE, .. "\r\nRCONPort=1\r\n"u8];

        var updated = (await LoadAsync(original)).Set("RCONPort", "2");

        Assert.Equal([.. "Name="u8, 0xFF, 0xFE, .. "\r\nRCONPort=2\r\n"u8], updated.Content);
    }

    [Theory]
    [InlineData("A=1\r\nB=2", "A=1\r\nB=2\r\nRCONPort=7\r\n")]
    [InlineData("A=1\n", "A=1\nRCONPort=7\n")]
    [InlineData("", "RCONPort=7\n")]
    public async Task Set_MissingKey_AppendsInTheFilesLineEnding(string content, string expected)
    {
        var updated = (await LoadAsync(Encoding.UTF8.GetBytes(content))).Set("RCONPort", "7");

        Assert.Equal(expected, Encoding.UTF8.GetString(updated.Content));
    }

    [Fact]
    public void Create_WritesOneLinePerEntry() =>
        Assert.Equal(
            "RCONPort=27015\nRCONPassword=x\n",
            Encoding.UTF8.GetString(ServerIniFile.Create([new("RCONPort", "27015"), new("RCONPassword", "x")])));
}
