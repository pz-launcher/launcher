using System.Text.Json.Nodes;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Launch;

public sealed class PzLaunchConfigEditorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ZombieBuddyJson => Fixtures.Read("Launch", "game-windows-zombiebuddy.json");

    private string Write(string json)
    {
        var path = Path.Combine(_temp.Path, "ProjectZomboid64.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static async Task<PzLaunchConfigEditor> LoadAsync(string path) => ResultAssert.Ok(await PzLaunchConfigEditor.LoadAsync(path, Ct));

    private static JsonNode? ParseFile(string path) => JsonNode.Parse(File.ReadAllText(path));

    private static JsonNode? WithTopLevelVmArgs(string json, params string[] vmArgs)
    {
        var node = JsonNode.Parse(json)!;
        node["vmArgs"] = new JsonArray([.. vmArgs.Select(arg => (JsonNode)JsonValue.Create(arg))]);
        return node;
    }

    [Fact]
    public async Task LoadAsync_ShippedJson_ReadsOnlyTopLevelVmArgs()
    {
        var editor = await LoadAsync(Write(GameInstallBuilder.WindowsGameJson));

        Assert.Equal(9, editor.VmArgs.Count);
        Assert.Equal("-Djava.awt.headless=true", editor.VmArgs[0]);
        Assert.DoesNotContain("-XX:+UseZGC", editor.VmArgs);
    }

    [Theory]
    [InlineData("game-windows.json")]
    [InlineData("game-windows-zombiebuddy.json")]
    public async Task SaveVmArgsAsync_Fixture_ChangesOnlyTopLevelVmArgs(string fixture)
    {
        var json = Fixtures.Read("Launch", fixture);
        var path = Write(json);
        var editor = await LoadAsync(path);
        string[] vmArgs = ["-agentlib:zbNative=verbosity=1", .. editor.VmArgs];

        ResultAssert.Ok(await editor.SaveVmArgsAsync(vmArgs, Ct));

        Assert.True(JsonNode.DeepEquals(WithTopLevelVmArgs(json, vmArgs), ParseFile(path)));
        Assert.Equal(vmArgs, (await LoadAsync(path)).VmArgs);
        Assert.Empty(Directory.EnumerateFiles(_temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task SaveVmArgsAsync_Change_WritesIndentedUtf8WithoutBomOrEscapes()
    {
        var path = Write(ZombieBuddyJson);
        var editor = await LoadAsync(path);

        ResultAssert.Ok(await editor.SaveVmArgsAsync(editor.VmArgs.Skip(1).ToList(), Ct));

        var bytes = await File.ReadAllBytesAsync(path, Ct);
        var text = await File.ReadAllTextAsync(path, Ct);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("\n  \"vmArgs\": [", text, StringComparison.Ordinal);
        Assert.Contains("\"-XX:+UseZGC\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Zoë\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveVmArgsAsync_SameArgs_DoesNotWrite()
    {
        var path = Write(ZombieBuddyJson);
        var editor = await LoadAsync(path);

        ResultAssert.Ok(await editor.SaveVmArgsAsync([.. editor.VmArgs], Ct));

        Assert.Equal(ZombieBuddyJson, await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task SaveVmArgsAsync_KeyInOtherCase_EditsThatKey()
    {
        var path = Write("""{ "mainClass": "m", "VMArgs": [ "-Xmx1g" ] }""");
        var editor = await LoadAsync(path);

        ResultAssert.Ok(await editor.SaveVmArgsAsync(["-agentlib:zbNative", .. editor.VmArgs], Ct));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "mainClass": "m", "VMArgs": [ "-agentlib:zbNative", "-Xmx1g" ] }"""), ParseFile(path)));
    }

    [Fact]
    public async Task SaveVmArgsAsync_NoVmArgs_AddsTheKey()
    {
        var path = Write("""{ "mainClass": "m" }""");
        var editor = await LoadAsync(path);
        Assert.Empty(editor.VmArgs);

        ResultAssert.Ok(await editor.SaveVmArgsAsync(["-agentlib:zbNative"], Ct));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "mainClass": "m", "vmArgs": [ "-agentlib:zbNative" ] }"""), ParseFile(path)));
    }

    [Fact]
    public async Task LoadAsync_CommentsAndTrailingCommas_AreAccepted()
    {
        var editor = await LoadAsync(Write("""
            {
              // comment
              "vmArgs": [ "-Xmx1g", ],
            }
            """));

        Assert.Equal(["-Xmx1g"], editor.VmArgs);
    }

    [Fact]
    public async Task LoadAsync_FileMissing_ReturnsConfigMissing()
    {
        var path = Path.Combine(_temp.Path, "nope", "ProjectZomboid64.json");

        var error = ResultAssert.Failed(await PzLaunchConfigEditor.LoadAsync(path, Ct), ErrorCode.LaunchConfigMissing);

        Assert.Equal(path, error.Details["path"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[ \"-Xmx1g\" ]")]
    [InlineData("""{ "vmArgs": "-Xmx1g" }""")]
    [InlineData("""{ "vmArgs": [ 1 ] }""")]
    [InlineData("""{ "vmArgs": [ null ] }""")]
    [InlineData("""{ "vmArgs": [], "vmargs": [] }""")]
    [InlineData("""{ "mainClass": "a", "mainClass": "b" }""")]
    public void Parse_Malformed_ReturnsConfigMalformed(string json) =>
        ResultAssert.Failed(PzLaunchConfigEditor.Parse(json, "/games/pz/ProjectZomboid64.json"), ErrorCode.LaunchConfigMalformed);

    [Fact]
    public async Task SaveVmArgsAsync_FileOpenWithoutDeleteSharing_ReturnsConfigWriteFailedAndLeavesNoTempFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace an open file.");
        var path = Write(GameInstallBuilder.WindowsGameJson);
        var editor = await LoadAsync(path);

        Error error;
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            error = ResultAssert.Failed(await editor.SaveVmArgsAsync(["-agentlib:zbNative"], Ct), ErrorCode.LaunchConfigWriteFailed);
        }

        Assert.Equal(path, error.Details["path"]);
        Assert.Equal(GameInstallBuilder.WindowsGameJson, await File.ReadAllTextAsync(path, Ct));
        Assert.Empty(Directory.EnumerateFiles(_temp.Path, "*.tmp"));
    }
}
