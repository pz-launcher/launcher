namespace PzLauncher.Core.Platform;

/// <summary>Disposing releases the handle; the process keeps running.</summary>
internal interface IRunningProcess : IDisposable
{
    int Id { get; }

    /// <summary>Valid once <see cref="WaitForExitAsync"/> has completed.</summary>
    int ExitCode { get; }

    /// <summary>The last lines the process wrote to stderr; complete once <see cref="WaitForExitAsync"/> has completed.</summary>
    IReadOnlyList<string> ErrorOutputTail { get; }

    Task WaitForExitAsync(CancellationToken ct);
}
