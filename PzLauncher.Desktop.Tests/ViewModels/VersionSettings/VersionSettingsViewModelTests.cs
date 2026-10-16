using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.VersionSettings;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestJava;

namespace PzLauncher.Desktop.Tests.ViewModels.VersionSettings;

[Collection(UiTextCultureCollection.Name)]
public class VersionSettingsViewModelTests : UiTextCultureTest
{
    private static readonly JavaRuntime UnknownVersionJava = Manual(@"D:\Tools\custom-jre");

    private readonly FakeInstallDetectionService _installDetection = new();
    private readonly FakeJavaDiscoveryService _javaDiscovery = new();
    private readonly FakeGameLaunchService _launcher = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly InstallsViewModel _installs;
    private readonly VersionSettingsViewModel _vm;

    public VersionSettingsViewModelTests()
    {
        _installs = new(_installDetection, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
        _installDetection.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, ManualGame, SteamServer));
        _javaDiscovery.ScanResults.Enqueue(new JavaScanResult([Jdk25, Jdk21, UnknownVersionJava], []));
        var java = ViewModel(Localization, _javaDiscovery, new FakePreferenceStore());
        _vm = new(_installs, _launcher, _overrides, java, new FakePlatformEnvironment(), Localization);
    }

    private async Task ActivateAsync(PzInstall? select = null)
    {
        await _installs.ActivateCommand.ExecuteAsync(null);
        if (select is not null)
        {
            _installs.ListSelection = select;
        }

        await _vm.ActivateCommand.ExecuteAsync(null);
    }

    private JavaOption Option(JavaRuntime runtime) =>
        _vm.JavaOptions.Single(option => option.HomePath == runtime.HomePath);

    private GcOption Gc(GarbageCollector? collector) => _vm.GcOptions.Single(option => option.Collector == collector);

    private static Task<Result> Fails(ErrorCode code) => Task.FromResult<Result>(TestErrors.Create(code, Jdk25.HomePath));

    [Fact]
    public async Task Activate_GameSelected_ShowsJsonValuesAndFollowsThem()
    {
        await ActivateAsync();

        Assert.Equal(SteamGame, _vm.Install);
        Assert.True(_vm.HasSettings);
        Assert.False(_vm.IsLoading);
        Assert.Null(_vm.ErrorText);
        Assert.Empty(_vm.Warnings);
        Assert.Equal(3072, _vm.JsonMaxMemoryMb);
        Assert.Null(_vm.JsonMinMemoryMb);
        Assert.Equal("-Xmx3072m -XX:+UseZGC", _vm.JsonVmArgs);
        Assert.Equal(16, _vm.SystemMemoryGb);
        Assert.Equal((2048, 16384), (_vm.MaxMemorySliderMinimum, _vm.MaxMemorySliderMaximum));
        Assert.Equal(MemoryMode.FollowJson, _vm.MaxMemoryMode);
        Assert.Equal(MemoryMode.FollowJson, _vm.MinMemoryMode);
        Assert.Equal(3072, _vm.CustomMaxMemoryMb);
        Assert.Equal(512, _vm.CustomMinMemoryMb);
        Assert.Equal((512, 3072), (_vm.MinMemorySliderMinimum, _vm.MinMemorySliderMaximum));
        Assert.Equal("", _vm.ExtraJvmArgs);
        Assert.Empty(_overrides.Saved);
    }

    [Fact]
    public async Task Activate_SavedOverrides_ShowsThem()
    {
        _overrides.Set(ManualGame.RootPath, new GameLaunchOverrides(Jdk21.HomePath, 8192, 4096, "-XX:+UseZGC", "-debug \"a b\"", GarbageCollector.G1));

        await ActivateAsync(ManualGame);

        Assert.Equal(ManualGame, _vm.Install);
        Assert.Same(Option(Jdk21), _vm.SelectedJava);
        Assert.Same(_vm.SelectedJava, _vm.JavaSelection);
        Assert.Equal((MemoryMode.Custom, 8192, 8.0), (_vm.MaxMemoryMode, _vm.CustomMaxMemoryMb, _vm.CustomMaxMemoryGb));
        Assert.Equal((MemoryMode.Custom, 4096, 4.0), (_vm.MinMemoryMode, _vm.CustomMinMemoryMb, _vm.CustomMinMemoryGb));
        Assert.Equal(8192, _vm.MinMemorySliderMaximum);
        Assert.Equal("-XX:+UseZGC", _vm.ExtraJvmArgs);
        Assert.Equal("-debug \"a b\"", _vm.ExtraGameArgs);
        Assert.Equal(GarbageCollector.G1, _vm.SelectedGc?.Collector);
        Assert.Empty(_overrides.Saved);
    }

    [Theory]
    [InlineData(32768, 20480)]
    [InlineData(1024, 256)]
    [InlineData(null, 4096)]
    [InlineData(256, null)]
    public async Task Activate_SavedMemoryOutsideTheUsualRange_SliderRangesSpanIt(int? max, int? min)
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { MaxMemoryMb = max, MinMemoryMb = min });

        await ActivateAsync();

        Assert.Equal((max ?? 3072, min ?? 512), (_vm.CustomMaxMemoryMb, _vm.CustomMinMemoryMb));
        Assert.InRange(_vm.CustomMaxMemoryMb, _vm.MaxMemorySliderMinimum, _vm.MaxMemorySliderMaximum);
        Assert.InRange(_vm.CustomMinMemoryMb, _vm.MinMemorySliderMinimum, _vm.MinMemorySliderMaximum);
        Assert.Empty(_overrides.Saved);
    }

    [Theory]
    [InlineData(null, 8192)]
    [InlineData(1024, 2048)]
    [InlineData(65536, 16384)]
    public async Task Activate_NoSavedMax_CustomStartsFromClampedJsonValue(int? jsonMax, int expected)
    {
        _launcher.Defaults = _ => TestLaunch.Defaults(maxMemoryMb: jsonMax);

        await ActivateAsync();

        Assert.Equal(expected, _vm.CustomMaxMemoryMb);
    }

    [Fact]
    public async Task Activate_JavaOptions_BundledFirstThenAvailableRuntimes()
    {
        await ActivateAsync();

        Assert.Equal(
            [(JavaOptionKind.Bundled, null), (JavaOptionKind.Runtime, Jdk25.HomePath), (JavaOptionKind.Runtime, Jdk21.HomePath), (JavaOptionKind.Runtime, UnknownVersionJava.HomePath)],
            _vm.JavaOptions.Select(option => (option.Kind, option.HomePath)));
        Assert.Same(_vm.JavaOptions[0], _vm.SelectedJava);
        Assert.Equal("Bundled with the game (Azul Systems, Inc. 25.0.1)", _vm.JavaOptions[0].Label);
        Assert.Equal(TestLaunch.BundledJava.HomePath, _vm.JavaOptions[0].PathText);
        Assert.Equal("Eclipse Adoptium 25.0.1", Option(Jdk25).Label);
        Assert.Equal("Unknown version", Option(UnknownVersionJava).Label);
    }

    [Fact]
    public async Task Activate_SavedJavaNoLongerAvailable_ListedAsMissingAndSelected()
    {
        const string removed = @"D:\Tools\jdk-17-removed";
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { JavaHome = removed });

        await ActivateAsync();

        var selected = Assert.IsType<JavaOption>(_vm.SelectedJava);
        Assert.Equal((JavaOptionKind.Missing, removed, true), (selected.Kind, selected.HomePath, selected.IsMissing));
        Assert.Same(_vm.JavaOptions[^1], selected);
        Assert.Equal("Not found", selected.Label);
        Assert.False(_vm.ShowJavaVersionWarning);
    }

    [Fact]
    public async Task JavaVersionWarning_ShownOnlyForAKnownOlderMajor()
    {
        await ActivateAsync();
        Assert.Equal(25, _vm.BundledJavaMajor);
        Assert.False(_vm.ShowJavaVersionWarning);

        _vm.JavaSelection = Option(Jdk21);
        Assert.True(_vm.ShowJavaVersionWarning);

        _vm.JavaSelection = Option(Jdk25);
        Assert.False(_vm.ShowJavaVersionWarning);

        _vm.JavaSelection = Option(UnknownVersionJava);
        Assert.False(_vm.ShowJavaVersionWarning);
    }

    [Fact]
    public async Task JavaVersionWarning_BundledVersionUnknown_NotShown()
    {
        _launcher.Defaults = _ => TestLaunch.Defaults() with { BundledJava = null };
        await ActivateAsync();

        _vm.JavaSelection = Option(Jdk21);

        Assert.Null(_vm.BundledJavaMajor);
        Assert.False(_vm.ShowJavaVersionWarning);
        Assert.Equal("Bundled with the game (Unknown version)", _vm.JavaOptions[0].Label);
    }

    [Fact]
    public async Task JavaSelection_Null_KeepsTheChoice()
    {
        await ActivateAsync();
        _vm.JavaSelection = Option(Jdk21);

        _vm.JavaSelection = null;

        Assert.Same(Option(Jdk21), _vm.SelectedJava);
    }

    [Fact]
    public async Task EachChange_SavesForThatInstallOnly()
    {
        _overrides.Set(SteamGame.RootPath, new GameLaunchOverrides(null, null, null, "", "-steam", null));
        await ActivateAsync(ManualGame);

        _vm.JavaSelection = Option(Jdk21);
        _vm.MaxMemoryMode = MemoryMode.Custom;
        _vm.CustomMaxMemoryMb = 6144;
        _vm.MinMemoryMode = MemoryMode.Custom;
        _vm.CustomMinMemoryMb = 1024;
        _vm.ExtraJvmArgs = "-XX:+UseZGC";
        _vm.ExtraGameArgs = "-debug";
        _vm.GcSelection = Gc(GarbageCollector.Shenandoah);
        _vm.JavaSelection = _vm.JavaOptions[0];

        Assert.All(_overrides.Saved, save => Assert.Equal(ManualGame.RootPath, save.Root));
        Assert.Equal(
            [
                new GameLaunchOverrides(Jdk21.HomePath, null, null, "", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 3072, null, "", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, null, "", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, 512, "", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, 1024, "", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, 1024, "-XX:+UseZGC", "", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, 1024, "-XX:+UseZGC", "-debug", null),
                new GameLaunchOverrides(Jdk21.HomePath, 6144, 1024, "-XX:+UseZGC", "-debug", GarbageCollector.Shenandoah),
                new GameLaunchOverrides(null, 6144, 1024, "-XX:+UseZGC", "-debug", GarbageCollector.Shenandoah),
            ],
            _overrides.Saved.Select(save => save.Overrides));
        Assert.Equal(new GameLaunchOverrides(null, null, null, "", "-steam", null), _overrides.Get(SteamGame.RootPath));
    }

    [Fact]
    public async Task SwitchInstall_ShowsEachInstallsOwnValues()
    {
        await ActivateAsync(ManualGame);
        _vm.MaxMemoryMode = MemoryMode.Custom;
        _vm.ExtraGameArgs = "-manual";

        _installs.ListSelection = SteamGame;
        await _vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(SteamGame, _vm.Install);
        Assert.Equal(MemoryMode.FollowJson, _vm.MaxMemoryMode);
        Assert.Equal("", _vm.ExtraGameArgs);

        _installs.ListSelection = ManualGame;
        await _vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(MemoryMode.Custom, _vm.MaxMemoryMode);
        Assert.Equal("-manual", _vm.ExtraGameArgs);
        Assert.Equal([ManualGame.RootPath], _overrides.Saved.Select(save => save.Root).Distinct());
    }

    [Fact]
    public async Task LoweringCustomMax_BelowCustomMin_PullsMinDown()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { MaxMemoryMb = 8192, MinMemoryMb = 6144 });
        await ActivateAsync();

        _vm.CustomMaxMemoryMb = 4096;

        Assert.Equal(4096, _vm.CustomMinMemoryMb);
        Assert.Equal(4096, _vm.MinMemorySliderMaximum);
        Assert.Equal(GameLaunchOverrides.Empty with { MaxMemoryMb = 4096, MinMemoryMb = 4096 }, _overrides.Saved[^1].Overrides);
    }

    [Fact]
    public async Task LoweringMax_BelowJsonMin_SwitchesMinToCustomAtMax()
    {
        _launcher.Defaults = _ => TestLaunch.Defaults(maxMemoryMb: 8192, minMemoryMb: 4096);
        await ActivateAsync();
        _vm.CustomMaxMemoryMb = 2048;

        _vm.MaxMemoryMode = MemoryMode.Custom;

        Assert.Equal((MemoryMode.Custom, 2048), (_vm.MinMemoryMode, _vm.CustomMinMemoryMb));
        Assert.Equal(GameLaunchOverrides.Empty with { MaxMemoryMb = 2048, MinMemoryMb = 2048 }, _overrides.Saved[^1].Overrides);
    }

    [Fact]
    public async Task RaisingMax_KeepsMin()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { MaxMemoryMb = 4096, MinMemoryMb = 2048 });
        await ActivateAsync();

        _vm.CustomMaxMemoryMb = 8192;

        Assert.Equal(2048, _vm.CustomMinMemoryMb);
    }

    [Fact]
    public async Task Activate_ConfigUnreadable_ShowsWarningAndCustomMemoryStillSaves()
    {
        var warning = TestErrors.Create(ErrorCode.LaunchConfigMalformed, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\ProjectZomboid64.json");
        _launcher.Defaults = _ => TestLaunch.Defaults(maxMemoryMb: null, minMemoryMb: null, warning) with { VmArgs = [] };

        await ActivateAsync();

        Assert.True(_vm.HasSettings);
        Assert.Equal([ErrorMessages.ToUserText(warning)], _vm.Warnings);
        Assert.Null(_vm.JsonMaxMemoryMb);
        Assert.Equal("", _vm.JsonVmArgs);

        _vm.MaxMemoryMode = MemoryMode.Custom;

        Assert.Equal(GameLaunchOverrides.Empty with { MaxMemoryMb = 8192 }, _overrides.Saved[^1].Overrides);
    }

    [Fact]
    public async Task Activate_DefaultsFail_ShowsErrorWithoutSettings()
    {
        var error = TestErrors.Create(ErrorCode.InstallPathNotFound, SteamGame.RootPath);
        _launcher.Defaults = _ => error;

        await ActivateAsync();

        Assert.Equal(SteamGame, _vm.Install);
        Assert.False(_vm.HasSettings);
        Assert.Equal(ErrorMessages.ToUserText(error), _vm.ErrorText);
    }

    [Fact]
    public async Task LanguageChanged_RerendersErrorAndWarnings()
    {
        var warning = TestErrors.Create(ErrorCode.LaunchConfigMissing, "ProjectZomboid64.json");
        _launcher.Defaults = _ => TestLaunch.Defaults(warnings: [warning]);
        await ActivateAsync();
        var english = _vm.Warnings[0];

        Localization.SetLanguage(AppLanguage.Russian);

        Assert.NotEqual(english, _vm.Warnings[0]);
        Assert.Equal(ErrorMessages.ToUserText(warning), _vm.Warnings[0]);
    }

    [Fact]
    public async Task Activate_ServerSelected_ShowsNothing()
    {
        await ActivateAsync(SteamServer);

        Assert.Null(_vm.Install);
        Assert.False(_vm.HasSettings);
        Assert.Null(_vm.ErrorText);
    }

    [Fact]
    public void Back_RaisesRequest()
    {
        var raised = 0;
        _vm.BackRequested += (_, _) => raised++;

        _vm.BackCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Activate_GcOptions_FollowJsonThenOfferedCollectors()
    {
        await ActivateAsync();

        Assert.Equal([null, GarbageCollector.G1, GarbageCollector.Z, GarbageCollector.Shenandoah], _vm.GcOptions.Select(option => option.Collector));
        Assert.Same(_vm.GcOptions[0], _vm.SelectedGc);
        Assert.Equal("Follow json (currently ZGC)", _vm.GcOptions[0].Label);
        Assert.Equal(["G1", "ZGC", "Shenandoah"], _vm.GcOptions.Skip(1).Select(option => option.Label));
    }

    [Theory]
    [InlineData(GarbageCollector.Shenandoah, true, "Follow json (currently Shenandoah, set by Viewpoint)")]
    [InlineData(GarbageCollector.Shenandoah, false, "Follow json (currently Shenandoah)")]
    [InlineData(null, false, "Follow json (not set)")]
    public async Task Activate_FollowJsonLabel_NamesTheJsonGcAndViewpoint(GarbageCollector? jsonGc, bool setByViewpoint, string label)
    {
        _launcher.Defaults = _ => TestLaunch.Defaults() with { GarbageCollector = jsonGc, GarbageCollectorSetByViewpoint = setByViewpoint };

        await ActivateAsync();

        Assert.Equal(label, _vm.GcOptions[0].Label);
    }

    [Fact]
    public async Task Activate_SavedGcThePageDoesNotOffer_ListedAndSelected()
    {
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { GarbageCollector = GarbageCollector.Parallel });

        await ActivateAsync();

        Assert.Equal(GarbageCollector.Parallel, _vm.SelectedGc?.Collector);
        Assert.Same(_vm.GcOptions[^1], _vm.SelectedGc);
        Assert.Empty(_overrides.Saved);
    }

    [Fact]
    public async Task GcSelection_Null_KeepsTheChoice()
    {
        await ActivateAsync();
        _vm.GcSelection = Gc(GarbageCollector.G1);

        _vm.GcSelection = null;

        Assert.Same(Gc(GarbageCollector.G1), _vm.SelectedGc);
    }

    [Fact]
    public async Task Activate_ValidatesTheSavedSettings()
    {
        var saved = GameLaunchOverrides.Empty with { JavaHome = Jdk25.HomePath, GarbageCollector = GarbageCollector.Shenandoah };
        _overrides.Set(SteamGame.RootPath, saved);

        await ActivateAsync();

        Assert.Equal([new LaunchGameRequest(SteamGame.RootPath, saved)], _launcher.Validations);
    }

    [Theory]
    [InlineData(ErrorCode.LaunchGcIncompatible)]
    [InlineData(ErrorCode.LaunchGcIncompatibleViewpoint)]
    [InlineData(ErrorCode.LaunchGcConflict)]
    public async Task GcProblem_ShownUnderGcAndClearedWhenGcChanges(ErrorCode code)
    {
        _launcher.Validate = _ => Fails(code);
        await ActivateAsync();

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(code, Jdk25.HomePath)), _vm.GcProblemText);
        Assert.Null(_vm.LaunchProblemText);

        _launcher.Validate = _ => Task.FromResult(Result.Ok());
        _vm.GcSelection = Gc(GarbageCollector.G1);

        Assert.Null(_vm.GcProblemText);
        Assert.Equal(GarbageCollector.G1, _launcher.Validations[^1].Overrides.GarbageCollector);
    }

    [Fact]
    public async Task GcProblem_ClearedWhenJavaChanges()
    {
        _launcher.Validate = request => request.Overrides.JavaHome == Jdk25.HomePath ? Fails(ErrorCode.LaunchGcIncompatible) : Task.FromResult(Result.Ok());
        await ActivateAsync();

        _vm.JavaSelection = Option(Jdk25);
        Assert.NotNull(_vm.GcProblemText);

        _vm.JavaSelection = Option(Jdk21);
        Assert.Null(_vm.GcProblemText);
    }

    [Fact]
    public async Task ExtraJvmArgsChange_IsValidated()
    {
        await ActivateAsync();
        _launcher.Validate = _ => Fails(ErrorCode.LaunchGcConflict);

        _vm.ExtraJvmArgs = "-XX:+UseG1GC";

        Assert.Equal("-XX:+UseG1GC", _launcher.Validations[^1].Overrides.ExtraJvmArgs);
        Assert.NotNull(_vm.GcProblemText);
    }

    [Fact]
    public async Task MemoryChange_IsNotValidated()
    {
        await ActivateAsync();

        _vm.MaxMemoryMode = MemoryMode.Custom;
        _vm.CustomMaxMemoryMb = 6144;

        Assert.Single(_launcher.Validations);
    }

    [Fact]
    public async Task OtherLaunchProblem_ShownAtTheTopNotUnderGc()
    {
        _launcher.Validate = _ => Fails(ErrorCode.LaunchJavaMissing);

        await ActivateAsync();

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.LaunchJavaMissing, Jdk25.HomePath)), _vm.LaunchProblemText);
        Assert.Null(_vm.GcProblemText);
    }

    [Fact]
    public async Task LaunchProblem_SameAsTheJsonWarning_NotRepeated()
    {
        var warning = TestErrors.Create(ErrorCode.LaunchConfigMalformed, "ProjectZomboid64.json");
        _launcher.Defaults = _ => TestLaunch.Defaults(warnings: [warning]);
        _launcher.Validate = _ => Task.FromResult<Result>(warning);

        await ActivateAsync();

        Assert.Single(_vm.Warnings);
        Assert.Null(_vm.LaunchProblemText);
    }

    [Fact]
    public async Task Validation_LatestChangeWins()
    {
        await ActivateAsync();
        var slow = new TaskCompletionSource<Result>();
        _launcher.Validate = _ => slow.Task;
        _vm.GcSelection = Gc(GarbageCollector.Shenandoah);

        _launcher.Validate = _ => Task.FromResult(Result.Ok());
        _vm.GcSelection = Gc(GarbageCollector.G1);
        slow.SetResult(TestErrors.Create(ErrorCode.LaunchGcIncompatible, Jdk25.HomePath));
        await Task.Yield();

        Assert.Null(_vm.GcProblemText);
    }

    [Fact]
    public async Task LanguageChanged_RerendersTheGcProblem()
    {
        var problem = TestErrors.Create(ErrorCode.LaunchGcIncompatible, Jdk25.HomePath);
        _launcher.Validate = _ => Task.FromResult<Result>(problem);
        await ActivateAsync();
        var english = _vm.GcProblemText;

        Localization.SetLanguage(AppLanguage.Russian);

        Assert.NotEqual(english, _vm.GcProblemText);
        Assert.Equal(ErrorMessages.ToUserText(problem), _vm.GcProblemText);
    }
}
