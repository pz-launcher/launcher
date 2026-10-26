using PzLauncher.Core.Common;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods.Modpacks;

public class PresetWriterTests
{
    private const string Root = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";

    private static readonly Modpack Server = new("Server", new ModpackPreset("Survival", ["Core"], IsLinked: true), new ModpackCollection.Steam(9000), []);

    private static readonly Modpack Copy = new("Copy", new ModpackPreset("Old", ["Cars"], IsLinked: false), new ModpackCollection.Steam(9001), []);

    private readonly FakeModCollectionService _service = new FakeModCollectionService().WithCollection("Survival", "Core").WithCollection("Spare", "Cars");
    private readonly FakeModCollectionDefaultsService _defaults = new();
    private readonly FakeModLibraryFile _file = new();
    private readonly ModpackStore _modpacks;
    private readonly PresetWriter _writer;

    public PresetWriterTests()
    {
        _modpacks = TestWorkshop.ModpackStoreOf(
            TestWorkshop.LocalCollectionStore(), _file, new Dictionary<string, IReadOnlyList<Modpack>> { [Root] = [Server, Copy] });
        _writer = new PresetWriter(_service, _defaults, _modpacks);
        _defaults.Save(Root, "Survival");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Modpack Saved(string name) => Assert.Single(_modpacks.For(Root), modpack => modpack.Name == name);

    [Fact]
    public async Task Save_Rename_TheLinksAndTheCurrentPresetFollowItInOneSave()
    {
        var saved = await _writer.SaveAsync(Root, "-debug", "Survival B42", ["Core"], "Survival", Ct);

        Assert.True(saved.IsSuccess);
        var request = Assert.Single(_service.Saves);
        Assert.Equal((Root, "-debug", "Survival", "Survival B42"), (request.InstallRoot, request.ExtraGameArgs, request.OriginalName, request.Name));
        Assert.Equal(new ModpackPreset("Survival B42", ["Core"], IsLinked: true), Saved("Server").Preset);
        Assert.Equal("Survival B42", _defaults.Get(Root));
        Assert.Single(_file.SavedModpacks);
    }

    [Fact]
    public async Task Save_SameName_ChangesNoLink()
    {
        await _writer.SaveAsync(Root, "", "Survival", ["Core", "Maps"], "Survival", Ct);

        Assert.Equal(["Core", "Maps"], _service.IdsOf("Survival"));
        Assert.Equal(Server, Saved("Server"));
        Assert.Equal("Survival", _defaults.Get(Root));
        Assert.Empty(_file.SavedModpacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_UnlinksOrDeletesItsUsersAndClearsTheCurrentPreset(bool deleteUsers)
    {
        var deleted = await _writer.DeleteAsync(Root, "", "Survival", deleteUsers, Ct);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(["Survival"], _service.Deletes);
        if (deleteUsers)
        {
            Assert.Equal([Copy], _modpacks.For(Root));
        }
        else
        {
            Assert.Equal(Server.Preset with { IsLinked = false }, Saved("Server").Preset);
        }

        Assert.Null(_defaults.Get(Root));
        Assert.Single(_file.SavedModpacks);
    }

    [Fact]
    public async Task Restore_WritesTheCopyAndLinksTheModpackToIt()
    {
        var restored = await _writer.RestoreAsync(Root, "", "Copy", "Old 2", ["Cars"], Ct);

        Assert.True(restored.IsSuccess);
        Assert.Equal(["Cars"], _service.IdsOf("Old 2"));
        Assert.Equal(new ModpackPreset("Old 2", ["Cars"], IsLinked: true), Saved("Copy").Preset);
        Assert.Equal("Survival", _defaults.Get(Root));
    }

    [Fact]
    public async Task FailedWrites_ChangeNoLinkAndNotTheCurrentPreset()
    {
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\saved_modlists.txt");
        _service.SaveError = _ => error;

        var renamed = await _writer.SaveAsync(Root, "", "Survival B42", ["Core"], "Survival", Ct);
        var restored = await _writer.RestoreAsync(Root, "", "Copy", "Old", ["Cars"], Ct);
        var deleted = await _writer.DeleteAsync(Root, "", "Missing", deleteUsers: true, Ct);

        Assert.Equal(ErrorCode.ModCollectionFileWriteFailed, renamed.Error?.Code);
        Assert.Equal(ErrorCode.ModCollectionFileWriteFailed, restored.Error?.Code);
        Assert.Equal(ErrorCode.ModCollectionNotFound, deleted.Error?.Code);
        Assert.Equal([Server, Copy], _modpacks.For(Root));
        Assert.Equal("Survival", _defaults.Get(Root));
        Assert.Empty(_file.SavedModpacks);
    }
}
