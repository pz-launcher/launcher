using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Java;
using static PzLauncher.Desktop.Tests.TestSupport.TestJava;

namespace PzLauncher.Desktop.Tests.ViewModels.Java;

[Collection(UiTextCultureCollection.Name)]
public class JavaRuntimesViewModelTests : UiTextCultureTest
{
    private const string ManualHome = @"D:\Tools\jre-17";

    private readonly FakeJavaDiscoveryService _service = new();
    private readonly FakeFolderPicker _picker = new();
    private readonly FakePreferenceStore _store = new();

    private JavaRuntimesViewModel CreateViewModel(params string[] savedManualHomes) =>
        new(_service, _picker, _store, new UserPreferences(null, null, null) { ManualJavaHomes = savedManualHomes }, new FakePlatformEnvironment(), Localization);

    private async Task<JavaRuntimesViewModel> CreateScannedViewModelAsync(params JavaRuntime[] found)
    {
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(found));
        var vm = CreateViewModel();
        await vm.EnsureScannedAsync();
        return vm;
    }

    private Task AddAsync(JavaRuntimesViewModel vm, string path)
    {
        _picker.PickNext(path);
        return vm.AddCommand.ExecuteAsync(null);
    }

    private static (string Home, JavaRuntimeSource Source, bool IsAvailable)[] Rows(JavaRuntimesViewModel vm) =>
        [.. vm.Runtimes.Select(row => (row.HomePath, row.Source, row.IsAvailable))];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureScannedAsync_Completes_GoesBusyThenLoadedOrEmpty(bool found)
    {
        JavaRuntime[] runtimes = found ? [Jdk25, Jdk21] : [];
        _service.ScanGate = new TaskCompletionSource();
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(runtimes));
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var scan = vm.EnsureScannedAsync();
        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.True(vm.HasScanned);
        Assert.Equal(!found, vm.IsEmpty);
        Assert.Contains(nameof(JavaRuntimesViewModel.IsEmpty), changed);
        Assert.Equal(runtimes, vm.AvailableRuntimes);
        Assert.Empty(vm.Warnings);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task EnsureScannedAsync_CalledAgainOrConcurrently_ScansOncePerSession()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();

        var first = vm.EnsureScannedAsync();
        var concurrent = vm.EnsureScannedAsync();
        _service.ScanGate.SetResult();
        await Task.WhenAll(first, concurrent);
        await vm.EnsureScannedAsync();

        Assert.Same(first, concurrent);
        Assert.Equal(1, _service.ScanCalls);
    }

    [Fact]
    public async Task ScanAsync_Rescan_ScansAgainAndReplacesDiscoveredRows()
    {
        var vm = await CreateScannedViewModelAsync(Jdk25, Jdk21);
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(Jdk21));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, _service.ScanCalls);
        Assert.Equal([Jdk21], vm.AvailableRuntimes);
    }

    [Fact]
    public async Task ScanAsync_Warnings_ShownAsUserTextAndReplacedOnRescan()
    {
        var warning = TestErrors.Create(ErrorCode.JavaSourceUnreadable, @"C:\Program Files\Java");
        _service.ScanResults.Enqueue(new JavaScanResult([Jdk21], [warning]));
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(Jdk21));
        var vm = CreateViewModel();

        await vm.EnsureScannedAsync();

        Assert.Equal([ErrorMessages.ToUserText(warning)], vm.Warnings);
        Assert.Equal([Jdk21], vm.AvailableRuntimes);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Warnings);
    }

    [Fact]
    public async Task ScanAsync_ServiceFails_SetsErrorText()
    {
        var error = TestErrors.Create(ErrorCode.ResultUninitialized);
        _service.ScanResults.Enqueue(Result<JavaScanResult>.Fail(error));
        var vm = CreateViewModel();

        await vm.EnsureScannedAsync();

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanAsync_Cancelled_ReturnsToIdleWithoutError()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();

        var scan = vm.ScanCommand.ExecuteAsync(null);
        vm.ScanCommand.Cancel();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.Null(vm.ErrorText);
        Assert.Empty(vm.Runtimes);
    }

    [Fact]
    public async Task ScanAsync_SavedManualHomes_InspectedAndMissingOneShownAsNotFound()
    {
        const string gone = @"E:\Old\jdk-11";
        _service.Inspect = path => path == gone ? TestErrors.Create(ErrorCode.JavaPathNotFound, path) : Manual(path);
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(Jdk21));
        var vm = CreateViewModel(ManualHome, gone);

        await vm.EnsureScannedAsync();

        Assert.Equal(
            [(Jdk21.HomePath, JavaRuntimeSource.JavaHome, true), (ManualHome, JavaRuntimeSource.Manual, true), (gone, JavaRuntimeSource.Manual, false)],
            Rows(vm));
        Assert.Equal([Jdk21, Manual(ManualHome)], vm.AvailableRuntimes);
        Assert.Equal([ManualHome, gone], _service.InspectedPaths);
        Assert.All(vm.Runtimes.Skip(1), row => Assert.True(row.IsManual));
    }

    [Fact]
    public void Constructor_SavedManualHomes_NotListedOrInspectedBeforeTheFirstScan()
    {
        var vm = CreateViewModel(ManualHome);

        Assert.Empty(vm.Runtimes);
        Assert.Empty(_service.InspectedPaths);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task AddAsync_ValidFolder_AddsManualRowAndSaves()
    {
        var vm = await CreateScannedViewModelAsync(Jdk21);
        _service.Inspect = _ => Manual(ManualHome);

        await AddAsync(vm, ManualHome + @"\bin");

        Assert.Equal([(Jdk21.HomePath, JavaRuntimeSource.JavaHome, true), (ManualHome, JavaRuntimeSource.Manual, true)], Rows(vm));
        Assert.Equal([ManualHome + @"\bin"], _service.InspectedPaths);
        Assert.Equal([ManualHome], Assert.Single(_store.SavedManualJavaHomes));
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task AddAsync_AlreadyManual_DoesNotDuplicateOrSaveAgain()
    {
        var vm = await CreateScannedViewModelAsync();
        await AddAsync(vm, ManualHome);

        await AddAsync(vm, ManualHome.ToUpperInvariant());

        Assert.Single(vm.Runtimes);
        Assert.Single(_store.SavedManualJavaHomes);
    }

    [Fact]
    public async Task AddAsync_DiscoveredHome_ShownOnceAsManualAndRemovingShowsItAsDiscoveredAgain()
    {
        var vm = await CreateScannedViewModelAsync(Jdk25, Jdk21);
        _service.Inspect = _ => Jdk25 with { Source = JavaRuntimeSource.Manual };

        await AddAsync(vm, Jdk25.HomePath);

        Assert.Equal([(Jdk25.HomePath, JavaRuntimeSource.Manual, true), (Jdk21.HomePath, JavaRuntimeSource.JavaHome, true)], Rows(vm));

        vm.Runtimes[0].RemoveCommand.Execute(null);

        Assert.Equal([(Jdk25.HomePath, JavaRuntimeSource.Registry, true), (Jdk21.HomePath, JavaRuntimeSource.JavaHome, true)], Rows(vm));
        Assert.Collection(
            _store.SavedManualJavaHomes,
            saved => Assert.Equal([Jdk25.HomePath], saved),
            saved => Assert.Empty(saved));
    }

    [Fact]
    public async Task AddAsync_MissingManualFolderPickedAgain_ShownAvailableWithoutSavingAgain()
    {
        _service.Inspect = path => TestErrors.Create(ErrorCode.JavaPathNotFound, path);
        var vm = CreateViewModel(ManualHome);
        await vm.EnsureScannedAsync();
        _service.Inspect = path => Manual(path);

        await AddAsync(vm, ManualHome);

        Assert.Equal([(ManualHome, JavaRuntimeSource.Manual, true)], Rows(vm));
        Assert.Empty(_store.SavedManualJavaHomes);
    }

    [Fact]
    public async Task AddAsync_InspectFailsThenSucceeds_ShowsThenClearsErrorText()
    {
        var error = TestErrors.Create(ErrorCode.JavaNot64Bit, @"D:\Old\jre-x86");
        _service.Inspect = _ => error;
        var vm = await CreateScannedViewModelAsync();

        await AddAsync(vm, @"D:\Old\jre-x86");

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Empty(vm.Runtimes);
        Assert.Empty(_store.SavedManualJavaHomes);

        _service.Inspect = path => Manual(path);
        await AddAsync(vm, ManualHome);

        Assert.Null(vm.ErrorText);
        Assert.Single(vm.Runtimes);
    }

    [Fact]
    public async Task AddAsync_PickerCancelled_ChangesNothing()
    {
        var vm = await CreateScannedViewModelAsync(Jdk21);

        _picker.Next = new FolderPickResult.Cancelled();
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal([Jdk21], vm.AvailableRuntimes);
        Assert.Empty(_service.InspectedPaths);
        Assert.Empty(_store.SavedManualJavaHomes);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task AddAsync_PickedFolderNotLocal_SetsErrorTextWithoutInspecting()
    {
        var vm = await CreateScannedViewModelAsync(Jdk21);
        _picker.Next = new FolderPickResult.NotLocal("smb://nas/java");

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.FolderNotLocal("smb://nas/java"), vm.ErrorText);
        Assert.Empty(_service.InspectedPaths);
    }

    [Fact]
    public async Task Remove_MissingManualRow_RemovesAndSaves()
    {
        _service.Inspect = path => TestErrors.Create(ErrorCode.JavaPathNotFound, path);
        _service.ScanResults.Enqueue(FakeJavaDiscoveryService.Found(Jdk21));
        var vm = CreateViewModel(ManualHome);
        await vm.EnsureScannedAsync();

        vm.Runtimes[1].RemoveCommand.Execute(null);

        Assert.Equal([(Jdk21.HomePath, JavaRuntimeSource.JavaHome, true)], Rows(vm));
        Assert.Empty(Assert.Single(_store.SavedManualJavaHomes));
    }

    [Fact]
    public async Task Remove_DiscoveredRow_IsIgnored()
    {
        var vm = await CreateScannedViewModelAsync(Jdk21);

        vm.Runtimes[0].RemoveCommand.Execute(null);

        Assert.Equal([Jdk21], vm.AvailableRuntimes);
        Assert.Empty(_store.SavedManualJavaHomes);
    }

    [Fact]
    public async Task Remove_LastRow_IsEmpty()
    {
        var vm = await CreateScannedViewModelAsync();
        await AddAsync(vm, ManualHome);

        vm.Runtimes[0].RemoveCommand.Execute(null);

        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task LanguageChanged_ShownErrorAndWarnings_AreReRenderedWithoutRescanning()
    {
        var warning = TestErrors.Create(ErrorCode.JavaReleaseUnreadable, @"C:\Java\jdk\release");
        var error = TestErrors.Create(ErrorCode.JavaNotAJavaHome, @"D:\Random");
        _service.ScanResults.Enqueue(new JavaScanResult([Jdk21], [warning]));
        _service.Inspect = _ => error;
        var vm = CreateViewModel();
        await vm.EnsureScannedAsync();
        await AddAsync(vm, @"D:\Random");
        var englishError = vm.ErrorText;
        var englishWarnings = vm.Warnings.ToList();

        Localization.SetLanguage(AppLanguage.Russian);

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.NotEqual(englishError, vm.ErrorText);
        Assert.Equal([ErrorMessages.ToUserText(warning)], vm.Warnings);
        Assert.NotEqual(englishWarnings, vm.Warnings);
        Assert.Equal(1, _service.ScanCalls);
    }
}
