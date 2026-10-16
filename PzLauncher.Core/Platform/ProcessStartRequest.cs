namespace PzLauncher.Core.Platform;

/// <param name="Environment">Added to, or replacing, the launcher's own variables.</param>
internal sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);
