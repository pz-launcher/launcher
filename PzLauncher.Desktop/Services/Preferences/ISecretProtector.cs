namespace PzLauncher.Desktop.Services.Preferences;

/// <summary>Turns a secret into the text saved in the preference file, prefixed with the scheme that wrote it.</summary>
public interface ISecretProtector
{
    /// <summary><c>null</c> when the OS refused to protect it; the secret is then not saved.</summary>
    string? Protect(string secret);

    /// <summary><c>null</c> for another scheme's text or text that can't be decrypted (another user or machine).</summary>
    string? Unprotect(string stored);
}
