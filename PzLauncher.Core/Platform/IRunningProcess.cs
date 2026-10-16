namespace PzLauncher.Core.Platform;

/// <summary>Disposing releases the handle; the process keeps running.</summary>
internal interface IRunningProcess : IDisposable
{
    int Id { get; }

    Task WaitForExitAsync(CancellationToken ct);
}
