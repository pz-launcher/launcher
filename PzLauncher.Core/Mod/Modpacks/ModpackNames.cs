namespace PzLauncher.Core.Mod.Modpacks;

/// <summary>The rule for modpack and local collection names.</summary>
public static class ModpackNames
{
    /// <summary>Non-blank and on one line, so share text can hold it on a line of its own; names are saved trimmed.</summary>
    public static bool IsValid(string name) => name.Trim().Length > 0 && !name.Any(char.IsControl);
}
