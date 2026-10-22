using System.Linq;

namespace PzLauncher.Desktop.Services.Mods;

public static class LocalCollectionName
{
    /// <summary>Non-blank and on one line; names are saved trimmed.</summary>
    public static bool IsValid(string name) => name.Trim().Length > 0 && !name.Any(char.IsControl);
}
