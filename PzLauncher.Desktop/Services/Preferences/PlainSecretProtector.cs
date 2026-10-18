using System;

namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>Linux and macOS: no keyring dependency, so the secret is saved as typed (the proxy page says so).</summary>
public sealed class PlainSecretProtector : ISecretProtector
{
    /// <summary>Persisted: never change it.</summary>
    public const string Prefix = "plain:";

    public string? Protect(string secret) => Prefix + secret;

    public string? Unprotect(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal) ? stored[Prefix.Length..] : null;
}
