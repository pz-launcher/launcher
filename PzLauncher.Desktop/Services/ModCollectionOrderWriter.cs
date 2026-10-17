using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Platform;

namespace PzLauncher.Desktop.Services;

public sealed class ModCollectionOrderWriter : IModCollectionOrderWriter
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly IModCollectionService _service;
    private readonly GameRunState _runState;
    private readonly IPlatformEnvironment _platform;
    private readonly TimeProvider _time;

    // Awaits resume on the caller's context, so this state is only touched on the UI thread.
    private readonly List<SaveModCollectionRequest> _writing = [];
    private Task<Result> _lastWrite = Task.FromResult(Result.Ok());
    private SaveModCollectionRequest? _queued;
    private CancellationTokenSource? _delay;

    public ModCollectionOrderWriter(IModCollectionService service, GameRunState runState, IPlatformEnvironment platform, TimeProvider time)
    {
        _service = service;
        _runState = runState;
        _platform = platform;
        _time = time;

        // Never unsubscribed: both are app-lifetime singletons.
        runState.PropertyChanged += OnRunStatePropertyChanged;
    }

    public event EventHandler<Result>? Written;

    public void Queue(string installRoot, string extraGameArgs, string name, IReadOnlyList<string> modIds)
    {
        // A newer order of the same collection replaces it; another collection's must not be lost.
        if (_queued is { } earlier && !Matches(earlier, installRoot, name) && !_runState.IsGameRunning)
        {
            _ = WriteAndReportAsync(earlier);
        }

        _queued = new SaveModCollectionRequest(installRoot, extraGameArgs, name, name, modIds);
        CancelDelay();
        var delay = new CancellationTokenSource();
        _delay = delay;
        _ = WriteLaterAsync(delay.Token);
    }

    public IReadOnlyList<string>? PendingOrder(string installRoot, string name) =>
        _queued is { } queued && Matches(queued, installRoot, name)
            ? queued.ModIds
            : _writing.LastOrDefault(request => Matches(request, installRoot, name))?.ModIds;

    public async Task<Result> FlushAsync(CancellationToken ct = default)
    {
        var own = _queued is { } queued && !_runState.IsGameRunning ? StartWrite(queued) : null;
        await _lastWrite.WaitAsync(ct);
        return own is null ? Result.Ok() : await own;
    }

    private bool Matches(SaveModCollectionRequest request, string installRoot, string name) =>
        request.Name == name && string.Equals(request.InstallRoot, installRoot, _platform.PathComparison);

    private async Task WriteLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SaveDelay, _time, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A flush can take the order between the delay ending and this continuation running.
        if (_queued is { } queued && !_runState.IsGameRunning)
        {
            await WriteAndReportAsync(queued);
        }
    }

    private async Task WriteAndReportAsync(SaveModCollectionRequest request)
    {
        var result = await StartWrite(request);
        Written?.Invoke(this, result);
    }

    private Task<Result> StartWrite(SaveModCollectionRequest request)
    {
        _queued = null;
        CancelDelay();
        _writing.Add(request);
        var write = WriteAfterAsync(_lastWrite, request);
        _lastWrite = write;
        return write;
    }

    // Each write waits for the one before, so an older order can't land last.
    private async Task<Result> WriteAfterAsync(Task previous, SaveModCollectionRequest request)
    {
        try
        {
            await previous;
            return await _service.SaveAsync(request);
        }
        finally
        {
            _writing.Remove(request);
        }
    }

    private void CancelDelay()
    {
        _delay?.Cancel();
        _delay?.Dispose();
        _delay = null;
    }

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The game may rewrite the file while it runs, so an order queued then waits for it to exit.
        if (e.PropertyName is nameof(GameRunState.IsGameRunning) && !_runState.IsGameRunning && _queued is { } queued)
        {
            _ = WriteAndReportAsync(queued);
        }
    }
}
