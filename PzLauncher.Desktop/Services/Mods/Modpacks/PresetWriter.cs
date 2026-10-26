using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Desktop.Services.Mods.Modpacks;

/// <summary>
/// Writes the install's mod presets, and after a write keeps the current preset and the modpacks linked to the preset in step. Callers read the
/// presets again afterwards; a link updated first is never taken for a preset gone from the files.
/// </summary>
public sealed class PresetWriter(IModCollectionService service, IModCollectionDefaultsService defaults, ModpackStore modpacks)
{
    /// <summary>Saves the preset; with <paramref name="original"/>, under its new name, and the current preset and the links follow it.</summary>
    public async Task<Result> SaveAsync(
        string installRoot, string extraGameArgs, string name, IReadOnlyList<string> modIds, string? original = null, CancellationToken ct = default)
    {
        var saved = await service.SaveAsync(new SaveModCollectionRequest(installRoot, extraGameArgs, original, name, modIds), ct);
        if (saved.IsSuccess && original is not null && original != name)
        {
            modpacks.RenamePreset(installRoot, original, name);
            if (defaults.Get(installRoot) == original)
            {
                defaults.Save(installRoot, name);
            }
        }

        return saved;
    }

    /// <param name="deleteUsers">The modpacks linked to it are deleted too; otherwise they keep their copy.</param>
    public async Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, bool deleteUsers, CancellationToken ct = default)
    {
        var deleted = await service.DeleteAsync(installRoot, extraGameArgs, name, ct);
        if (!deleted.IsSuccess)
        {
            return deleted;
        }

        if (deleteUsers)
        {
            modpacks.Delete(installRoot, modpacks.UsersOfPreset(installRoot, name));
        }
        else
        {
            modpacks.UnlinkPreset(installRoot, name);
        }

        if (defaults.Get(installRoot) == name)
        {
            defaults.Clear(installRoot);
        }

        return deleted;
    }

    /// <summary>Writes the modpack's preset copy under <paramref name="name"/> and links the modpack to that preset.</summary>
    public async Task<Result> RestoreAsync(
        string installRoot, string extraGameArgs, string modpack, string name, IReadOnlyList<string> modIds, CancellationToken ct = default)
    {
        var saved = await SaveAsync(installRoot, extraGameArgs, name, modIds, ct: ct);
        if (saved.IsSuccess)
        {
            modpacks.LinkPreset(installRoot, modpack, name);
        }

        return saved;
    }
}
