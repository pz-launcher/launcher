# Mod library

> The launcher's local collections and modpacks. Reference: `PzLauncher.Desktop/Services/Mods/Library/IModLibraryFile.cs`, `JsonModLibraryFile.cs`.

---

## Rules

- `<ApplicationData>/PzLauncher/mod-library.json` is read and written only through `IModLibraryFile`, by `LocalCollectionStore` and `ModpackStore`. They convert to and from the saved records (`SavedLocalCollection`, `SavedModpack`) at that boundary.
- Its own file, not `preferences.json`: launchers released before it drop unknown `preferences.json` fields when they save.
- Loaded once, synchronously, at startup. Unknown top-level fields are kept and written back.
- `ModLibrary.LocalCollectionsComplete` is true only when the file is missing or was read and `localCollections` is missing or an array from which nothing was dropped. Only then does `ModpackStore` make a modpack linked to a missing local collection independent at startup; otherwise the link stays until that collection is deleted in the launcher.
- An unreadable file (I/O error, bad JSON, root not an object) loads empty and is copied to `mod-library.unreadable-<UTC yyyyMMddHHmmss>.json` beside it before the first write replaces it. A failed copy skips that write; the next save tries again.
- Saves are fire and forget, never throw for I/O (logged), and rewrite the whole file through a temp file and a move. One write runs at a time; saves made while it runs are merged into one following write of the latest state. A caller's token only stops its wait.
- `ModpackStore` saves every install's modpacks in one call, so one change writes `modpacks` once.
- Every preset save, rename, delete and restore goes through `ModsViewModel`'s preset entry points (`SavePresetAsync`, ...) over `PresetWriter`, which update the current preset and the modpacks' links before the page reads the presets again. View models never call `ModpackStore`'s link methods.
- A modpack import writes through `ModpackImport` inside the page's write; a name taken since the user chose it is a `NameTaken` result, never an exception. Extra items are recomputed only by `ModpackStore.RefreshExtraItems`.

## File contract (persisted: never change an existing value)

An item is `{id, title}`: `id` decimal text, `title` omitted when unknown.

| Field | Values | Record |
|---|---|---|
| `localCollections` | array, user order, of `{id, name, items}`: `id` a GUID (`D` form), never changed by a rename; `name` unique (ordinal), non-blank, one line, saved trimmed; `items` an array of items in the user's order. An entry with a bad id, name or item list, or repeating an earlier id or name, is dropped; a bad or repeated item is dropped from its entry; the rest kept | `SavedLocalCollection` |
| `modpacks` | object: absolute install root → array, user order, of `{name, preset, collection, extraItems}`. `name` unique per install (ordinal), non-blank, one line, saved trimmed. `preset`: `{linked, copy: {name, modIds}}`, `linked` the name of the install's preset the copy follows, missing when independent. `collection`: `{steam: id}` or `{local: {linked, copy: {name, items}}}`, `linked` the followed local collection's `id`, missing when independent. `extraItems`: item ids. Ids are decimal text. A non-object loads none; a bad root or non-array is dropped; a modpack with a bad name, preset or collection, or repeating an earlier name, is dropped; a bad or repeated id is dropped from its modpack; the rest kept. An install without modpacks has no entry | `SavedModpack` |

## Tests

Reference: `PzLauncher.Desktop.Tests/Services/Mods/Library/JsonModLibraryFileTests.cs`. Each field: round-trip, invalid entries, saving it keeps the other; plus completeness, unreadable copy-aside, unknown fields, unwritable file and merged saves.
