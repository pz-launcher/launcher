namespace PzLauncher.Core.Server.ConsoleLog;

/// <param name="Text">Empty for <see cref="ServerConsoleLineKind.FileRestarted"/>.</param>
public sealed record ServerConsoleLine(string Text, ServerConsoleLineKind Kind);
