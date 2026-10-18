using PzLauncher.Core.Common;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Mods;
using static PzLauncher.Desktop.Tests.TestSupport.TestMods;

namespace PzLauncher.Desktop.Tests.ViewModels.Mods;

[Collection(UiTextCultureCollection.Name)]
public class ModCollectionApplierTests : UiTextCultureTest
{
    private const string Root = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";

    private readonly FakeModCollectionService _service = new();
    private readonly FakeDialogService _dialogs = new();

    public ModCollectionApplierTests()
    {
        _service
            .WithMods(Mod("ModA"), Mod("ModB", name: "Mod B", require: ["ModA"]), Mod("ModC", name: "Mod C", incompatible: ["ModB"]), Mod("ModD"))
            .WithCollection("Pack", "ModB", "ModD")
            .WithCollection("Clashing", "ModB", "ModC", "Gone");
    }

    private Task<ModCollectionApplyOutcome> ApplyAsync(string name = "Pack") => new ModCollectionApplier(_service, _dialogs).ApplyAsync(Root, "-debug", name);

    [Fact]
    public async Task Apply_ActiveListMatchesACollection_WritesWithoutAsking()
    {
        _service.ActiveIds = ["ModA", "ModB", "ModD"];

        var outcome = await ApplyAsync();

        var applied = Assert.IsType<ModCollectionApplyOutcome.Applied>(outcome);
        Assert.Equal(["ModA", "ModB", "ModD"], applied.Result.EnabledIds);
        Assert.Equal([(Root, "-debug", "Pack")], _service.Applies);
        Assert.Empty(_dialogs.Prompts);
        Assert.Empty(_dialogs.Confirms);
    }

    [Fact]
    public async Task Apply_NothingEnabled_WritesWithoutAsking()
    {
        Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync());

        Assert.Empty(_dialogs.Prompts);
    }

    [Fact]
    public async Task Apply_ActiveListMatchesNoCollection_OffersToSaveItPrefilled()
    {
        _service.ActiveIds = ["ModD", "ModA"];

        await ApplyAsync();

        var prompt = Assert.Single(_dialogs.Prompts);
        Assert.Equal(UiText.ModApply_KeepDefaultName, prompt.InitialText);
        Assert.Equal(UiText.ModApply_KeepSkip, prompt.SkipText);
        Assert.Null(prompt.Validate(UiText.ModApply_KeepDefaultName));
        Assert.NotNull(prompt.Validate("bad:name"));
        Assert.NotNull(prompt.Validate("  "));
    }

    [Fact]
    public async Task Apply_SaveChosen_StoresTheActiveListInItsOrderThenApplies()
    {
        _service.ActiveIds = ["ModD", "Unknown", "ModA"];
        _dialogs.Accept("  Before  ");

        Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync());

        Assert.Equal(["ModD", "Unknown", "ModA"], _service.IdsOf("Before"));
        Assert.Single(_service.Applies);
    }

    [Fact]
    public async Task Apply_SaveUnderAnExistingName_AsksBeforeOverwriting_DeclinedAsksForTheNameAgain()
    {
        _service.ActiveIds = ["ModD"];
        _dialogs.Accept("Clashing");
        _dialogs.ConfirmAnswers.Enqueue(false);
        _dialogs.Accept("Fresh");

        Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync());

        Assert.Single(_dialogs.Confirms);
        Assert.Equal(2, _dialogs.Prompts.Count);
        Assert.Equal("Clashing", _dialogs.Prompts[1].InitialText);
        Assert.Equal(["ModB", "ModC", "Gone"], _service.IdsOf("Clashing"));
        Assert.Equal(["ModD"], _service.IdsOf("Fresh"));
    }

    [Fact]
    public async Task Apply_SaveOverTheAppliedCollection_AppliesTheSavedList()
    {
        _service.ActiveIds = ["ModD"];
        _dialogs.Accept("Pack");
        _dialogs.ConfirmAnswers.Enqueue(true);

        var applied = Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync());

        Assert.Equal(["ModD"], applied.Result.EnabledIds);
    }

    [Fact]
    public async Task Apply_SkipChosen_AppliesWithoutSaving()
    {
        _service.ActiveIds = ["ModD"];
        _dialogs.PromptAnswers.Enqueue(new TextPromptResult.Skipped());

        Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync());

        Assert.Empty(_service.Saves);
        Assert.Single(_service.Applies);
    }

    [Fact]
    public async Task Apply_KeepPromptCancelled_WritesNothing()
    {
        _service.ActiveIds = ["ModD"];

        Assert.IsType<ModCollectionApplyOutcome.Cancelled>(await ApplyAsync());

        Assert.Empty(_service.Saves);
        Assert.Empty(_service.Applies);
        Assert.Equal(["ModD"], _service.ActiveIds);
    }

    [Fact]
    public async Task Apply_DropsMods_ListsMissingAndSkippedInOneNoticeBeforeWriting()
    {
        _dialogs.ConfirmAnswers.Enqueue(true);

        var applied = Assert.IsType<ModCollectionApplyOutcome.Applied>(await ApplyAsync("Clashing"));

        var notice = Assert.Single(_dialogs.Confirms);
        Assert.Equal(
            [
                string.Format(UiText.ModApply_DropMissing, "Gone"),
                string.Format(UiText.ModApply_DropIncompatible, string.Format(UiText.Mods_NameWithId, "Mod C", "ModC"), string.Format(UiText.Mods_NameWithId, "Mod B", "ModB")),
            ],
            notice.Items);
        Assert.Equal(["ModA", "ModB"], applied.Result.EnabledIds);
    }

    [Fact]
    public async Task Apply_DropNoticeCancelled_WritesNothing()
    {
        Assert.IsType<ModCollectionApplyOutcome.Cancelled>(await ApplyAsync("Clashing"));

        Assert.Single(_dialogs.Confirms);
        Assert.Empty(_service.Applies);
    }

    [Fact]
    public async Task Apply_ReadFails_ReturnsTheErrorWithoutAsking()
    {
        var error = TestErrors.Create(ErrorCode.ModCollectionFileUnreadable, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _service.GetError = () => error;

        var failed = Assert.IsType<ModCollectionApplyOutcome.Failed>(await ApplyAsync());

        Assert.Equal(error, failed.Error);
        Assert.Empty(_dialogs.Prompts);
        Assert.Empty(_service.Applies);
    }

    [Fact]
    public async Task Apply_CollectionGone_ReturnsNotFound()
    {
        var failed = Assert.IsType<ModCollectionApplyOutcome.Failed>(await ApplyAsync("Deleted in-game"));

        Assert.Equal(ErrorCode.ModCollectionNotFound, failed.Error.Code);
        Assert.Empty(_dialogs.Confirms);
    }
}
