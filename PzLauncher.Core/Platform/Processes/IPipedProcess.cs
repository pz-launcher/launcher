namespace PzLauncher.Core.Platform.Processes;

/// <summary>A started process whose stdin and stdout belong to the caller (UTF-8, no BOM); stderr is drained like any other.</summary>
internal interface IPipedProcess : IRunningProcess
{
    /// <summary>Writes the text to stdin, then closes it.</summary>
    Task SendInputAsync(string text, CancellationToken ct);

    /// <summary>The next stdout line; <c>null</c> once stdout is closed.</summary>
    Task<string?> ReadLineAsync(CancellationToken ct);

    /// <summary>Kills the process and its children; does nothing once it exited.</summary>
    void Kill();
}
