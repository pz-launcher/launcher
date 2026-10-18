# Group Desktop Services into feature folders

## Goal

`PzLauncher.Desktop/Services/` holds 41 flat files after the auto-update and
proxy work. ViewModels/ and Views/ are already split by feature; give Services/
the same shape so a feature's UI services are found next to each other.

## Scope

Pure move, no behavior change. Each file moves with `git mv` and its namespace
becomes `PzLauncher.Desktop.Services.<Folder>`; consumers get the new `using`s.

| Folder | Files |
|---|---|
| `Shell/` | IAppLifetime, AvaloniaAppLifetime, IClipboardService, AvaloniaClipboardService, IDialogService, AvaloniaDialogService, ConfirmPrompt, TextPrompt, IFolderPicker, AvaloniaFolderPicker, FolderPickResult, IThemeService, AvaloniaThemeService, IUrlLauncher, AvaloniaUrlLauncher, IWindowPlacementService, WindowPlacement, ContextProgress |
| `Preferences/` | IPreferenceStore, JsonPreferenceStore, DesktopJsonContext, SavedProxy, ISecretProtector, PlainSecretProtector, WindowsDpapiSecretProtector, GarbageCollectorPreferences, ProxyModePreferences, UpdateSourcePreferences, ZombieBuddyChannelPreferences |
| `Launch/` | GameRunState, IInstallOverridesService, InstallOverridesService |
| `Mods/` | IModCollectionDefaultsService, ModCollectionDefaultsService, IModCollectionOrderWriter, ModCollectionOrderWriter |
| `ZombieBuddy/` | IZombieBuddyChannelService, ZombieBuddyChannelService |
| `Update/` | UpdateCoordinator |
| `Net/` | IProxySettingsService, ProxySettingsService |

- `PzLauncher.Desktop.Tests/Services/*` moves to the mirrored folders
  (test folders mirror source folders, `.trellis/spec/common/test-strategy.md`),
  test namespaces follow.
- Spec references to these files (`.trellis/spec/desktop/*.md`,
  `.trellis/spec/common/*.md`) point at the new paths; the desktop layout tree
  shows `Services/<Feature>/`.

## Out of scope

- Renaming types, changing visibility, splitting or merging files.
- Moving anything outside `Services/` (e.g. `Views/WindowPlacementPersistence.cs`).

## Acceptance criteria

- [ ] No `.cs` file left directly under `PzLauncher.Desktop/Services/` or
      `PzLauncher.Desktop.Tests/Services/`.
- [ ] Every moved file's namespace matches its folder.
- [ ] `dotnet build PzLauncher.slnx -c Release` passes (warnings as errors).
- [ ] `dotnet test PzLauncher.slnx -c Release` passes.
- [ ] `grep -rn "Desktop/Services/[A-Z][A-Za-z]*\.cs" .trellis/spec` returns nothing
      (only `Services/<Folder>/...` paths remain).
- [ ] One commit on its own branch, PR against `main` (Why / What changed / Testing).
