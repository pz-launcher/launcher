namespace PzLauncher.Desktop.Services;

/// <summary>Per-install current mod collection, applied before every launch; looked up with the platform's path comparison and persisted through <see cref="IPreferenceStore"/>.</summary>
public interface IModCollectionDefaultsService
{
    /// <summary><c>null</c> when none is marked.</summary>
    string? Get(string installRoot);

    /// <summary>Marks this collection, moving the mark from any other. Applies at once; the file write is fire-and-forget.</summary>
    void Save(string installRoot, string name);

    /// <inheritdoc cref="Save"/>
    void Clear(string installRoot);
}
