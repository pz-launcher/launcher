using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

public sealed class SteamLoginUsersTests : IDisposable
{
    private const string FilePath = "loginusers.vdf";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Fixture => Fixtures.ReadLf("Steam", "loginusers.vdf");

    private static Result<SteamAccount> Parse(string text) => SteamLoginUsers.ParseCurrent(text, FilePath);

    [Fact]
    public void ParseCurrent_NoMostRecent_IsTheLatestSignIn() =>
        Assert.Equal(new SteamAccount(629668875, "Example Player"), ResultAssert.Ok(Parse(Fixture)));

    [Fact]
    public void ParseCurrent_MostRecent_WinsOverTheLatestSignIn()
    {
        var text = Fixture.Replace("\"AutoLogin\"\t\t\"0\"", "\"MostRecent\"\t\t\"1\"", StringComparison.Ordinal);

        Assert.Equal(new SteamAccount(629668886, "Second Player"), ResultAssert.Ok(Parse(text)));
    }

    [Theory]
    [InlineData(false, "example_login_a")]
    [InlineData(true, "629668875")]
    public void ParseCurrent_NoPersonaName_FallsBackToTheLoginThenTheId(bool withoutLogin, string expected)
    {
        var text = Fixture.Replace("\t\t\"PersonaName\"\t\t\"Example Player\"\n", "", StringComparison.Ordinal);
        if (withoutLogin)
        {
            text = text.Replace("\t\t\"AccountName\"\t\t\"example_login_a\"\n", "", StringComparison.Ordinal);
        }

        Assert.Equal(expected, ResultAssert.Ok(Parse(text)).PersonaName);
    }

    [Theory]
    [InlineData("\"users\"\n{\n}\n")]
    [InlineData("\"users\"\n{\n\t\"not-a-steam-id\"\n\t{\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n")]
    [InlineData("\"other\"\n{\n}\n")]
    [InlineData("\"users\"\n{\n")]
    public void ParseCurrent_NoUsableUser_IsNoCurrentAccount(string text) =>
        Assert.Equal(FilePath, ResultAssert.Failed(Parse(text), ErrorCode.SteamNoCurrentAccount).Details["path"]);

    [Fact]
    public async Task ReadCurrentAsync_MissingFile_IsNoCurrentAccount() =>
        ResultAssert.Failed(
            await SteamLoginUsers.ReadCurrentAsync(Path.Combine(_temp.Path, "loginusers.vdf"), TestContext.Current.CancellationToken),
            ErrorCode.SteamNoCurrentAccount);
}
