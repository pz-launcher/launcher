using PzLauncher.Core.Common;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class ModCollectionOrderWriterTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";
    private const string GameArgs = @"-cachedir=D:\pz";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly FakeModCollectionService _service = new FakeModCollectionService()
        .WithCollection("Survival", "Maps", "Core", "Cars")
        .WithCollection("Driving", "Cars", "Tweaks");

    private readonly GameRunState _runState = new();
    private readonly ManualTimeProvider _time = new();
    private readonly List<Result> _written = [];

    private ModCollectionOrderWriter CreateWriter()
    {
        var writer = new ModCollectionOrderWriter(_service, _runState, new FakePlatformEnvironment(), _time);
        writer.Written += (_, result) => _written.Add(result);
        return writer;
    }

    [Fact]
    public void Queue_WritesTheLatestOrderOnceAfterTheDelay()
    {
        var writer = CreateWriter();

        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);
        _time.Advance(SaveDelay / 2);
        writer.Queue(GameRoot, GameArgs, "Survival", ["Cars", "Core", "Maps"]);
        _time.Advance(SaveDelay / 2);

        Assert.Empty(_service.Saves);
        Assert.Equal(["Cars", "Core", "Maps"], writer.PendingOrder(GameRoot, "Survival"));

        _time.Advance(SaveDelay / 2);

        var save = Assert.Single(_service.Saves);
        Assert.Equal((GameRoot, GameArgs, "Survival", "Survival"), (save.InstallRoot, save.ExtraGameArgs, save.OriginalName, save.Name));
        Assert.Equal(["Cars", "Core", "Maps"], _service.IdsOf("Survival"));
        Assert.True(Assert.Single(_written).IsSuccess);
        Assert.Null(writer.PendingOrder(GameRoot, "Survival"));

        _time.Advance(SaveDelay * 2);

        Assert.Single(_service.Saves);
    }

    [Fact]
    public void Queue_AnotherCollection_WritesTheEarlierOrderAtOnce()
    {
        var writer = CreateWriter();

        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);
        writer.Queue(GameRoot, GameArgs, "Driving", ["Tweaks", "Cars"]);

        Assert.Equal(["Core", "Maps", "Cars"], _service.IdsOf("Survival"));
        Assert.Equal(["Cars", "Tweaks"], _service.IdsOf("Driving"));

        _time.Advance(SaveDelay);

        Assert.Equal(["Survival", "Driving"], _service.Saves.Select(save => save.Name));
        Assert.Equal(["Tweaks", "Cars"], _service.IdsOf("Driving"));
    }

    [Fact]
    public void PendingOrder_OnlyForThatCollectionOfThatInstall()
    {
        var writer = CreateWriter();

        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);

        Assert.Equal(["Core", "Maps", "Cars"], writer.PendingOrder(GameRoot.ToUpperInvariant(), "Survival"));
        Assert.Null(writer.PendingOrder(GameRoot, "Driving"));
        Assert.Null(writer.PendingOrder(OtherRoot, "Survival"));
    }

    [Fact]
    public async Task FlushAsync_Queued_WritesNowAndOnlyOnce()
    {
        var writer = CreateWriter();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);

        var result = await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Core", "Maps", "Cars"], _service.IdsOf("Survival"));
        Assert.Empty(_written);

        _time.Advance(SaveDelay);

        Assert.Single(_service.Saves);
    }

    [Fact]
    public async Task FlushAsync_NothingQueued_CompletesWithoutWriting()
    {
        var result = await CreateWriter().FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_service.Saves);
    }

    [Fact]
    public async Task FlushAsync_WriteFails_ReturnsTheError()
    {
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _service.SaveError = _ => error;
        var writer = CreateWriter();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);

        var result = await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(error, result.Error);
        Assert.Empty(_written);
        Assert.Null(writer.PendingOrder(GameRoot, "Survival"));
    }

    [Fact]
    public void DelayedWriteFails_RaisesTheError()
    {
        var error = TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\pz_modlist_settings.cfg");
        _service.SaveError = _ => error;
        var writer = CreateWriter();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);

        _time.Advance(SaveDelay);

        Assert.Equal(error, Assert.Single(_written).Error);
    }

    [Fact]
    public async Task FlushAsync_WriteInFlight_CompletesWhenItFinishes_AndKeepsShowingItsOrder()
    {
        var writer = CreateWriter();
        _service.SaveGate = new TaskCompletionSource();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);
        _time.Advance(SaveDelay);

        var flush = writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.False(flush.IsCompleted);
        Assert.Equal(["Core", "Maps", "Cars"], writer.PendingOrder(GameRoot, "Survival"));

        _service.SaveGate.SetResult();
        var result = await flush;

        Assert.True(result.IsSuccess);
        Assert.Single(_service.Saves);
        Assert.Single(_written);
        Assert.Null(writer.PendingOrder(GameRoot, "Survival"));
    }

    [Fact]
    public async Task FlushAsync_WriteInFlight_WritesTheNewerOrderAfterIt()
    {
        var writer = CreateWriter();
        _service.SaveGate = new TaskCompletionSource();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);
        _time.Advance(SaveDelay);
        writer.Queue(GameRoot, GameArgs, "Survival", ["Cars", "Maps", "Core"]);

        var flush = writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Single(_service.Saves);
        Assert.Equal(["Cars", "Maps", "Core"], writer.PendingOrder(GameRoot, "Survival"));

        _service.SaveGate.SetResult();
        await flush;

        Assert.Equal(2, _service.Saves.Count);
        Assert.Equal(["Cars", "Maps", "Core"], _service.IdsOf("Survival"));
    }

    [Fact]
    public async Task FlushAsync_Cancelled_Throws()
    {
        var writer = CreateWriter();
        _service.SaveGate = new TaskCompletionSource();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.FlushAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task GameRunning_HoldsTheWriteUntilTheGameExits()
    {
        var writer = CreateWriter();
        writer.Queue(GameRoot, GameArgs, "Survival", ["Core", "Maps", "Cars"]);
        _runState.IsGameRunning = true;

        _time.Advance(SaveDelay);
        var flushed = await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(flushed.IsSuccess);
        Assert.Empty(_service.Saves);
        Assert.Equal(["Core", "Maps", "Cars"], writer.PendingOrder(GameRoot, "Survival"));

        _runState.IsGameRunning = false;

        Assert.Equal(["Core", "Maps", "Cars"], _service.IdsOf("Survival"));
        Assert.True(Assert.Single(_written).IsSuccess);
    }
}
