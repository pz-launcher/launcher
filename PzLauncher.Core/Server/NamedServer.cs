namespace PzLauncher.Core.Server;

/// <param name="HasDatabase">PZ's player database exists. A first start that failed leaves one without an admin account.</param>
public sealed record NamedServer(string Name, bool HasWorld, bool HasDatabase);
