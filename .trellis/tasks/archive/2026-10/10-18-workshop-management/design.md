# Design — Steam Workshop management

## Boundaries

| Layer | Owns |
|-------|------|
| `PzLauncher.Core/Steam/Workshop/` | Local Workshop files, Web API client, Workshop state model, helper protocol, flat-API binding, helper runner |
| `PzLauncher.Desktop` | Helper entry dispatch in `Program.Main`, the two Mods sections, dialogs, pre-launch check in Home, remembered collections preference |

Core stays UI-free; every service returns `Result`/`Result<T>` (core/error-handling.md) with new `WorkshopErrors` codes.

## Read side (no Steamworks)

- `WorkshopSubscriptionsFile` — parses `<SteamRoot>/userdata/<accountid>/ugc/108600_subscriptions.vdf` (account from
  `SteamLoginUsers`). Untrusted input: malformed entries are skipped and logged.
- `WorkshopManifestFile` — parses `appworkshop_108600.acf` of every library `WorkshopLibraries.CandidatesAsync` returns
  (never the copy inside the install folder); per item: installed manifest, timeupdated, size.
- `WorkshopWebApi` — POSTs `GetPublishedFileDetails` (batches of ≤ 1000 ids) and `GetCollectionDetails` through
  `LauncherHttpClient` (proxy-aware); System.Text.Json source-generated context (AOT). Timeout per request.
- `IWorkshopService`
  - `GetSubscriptionsAsync(installRoot)` → `WorkshopItem` list: id, title?, installed manifest, size, timeupdated,
    `disabled_locally`, mod ids (from `ModLocator` reading the item folder), state.
  - `CheckUpdatesAsync(ids)` → latest `hcontent_file` / `time_updated` / title per id; outdated = installed manifest ≠
    latest. Results cached in memory for the session (the server caches ~30 min anyway).
  - `GetCollectionAsync(id)` → title + ordered children, nested collections expanded depth-first with a visited set
    (cycle-safe), then details for all children.
- Collection usage of an item = launcher mod collections (`IModCollectionService.GetAsync`) whose ids intersect the
  item's mod ids.

As built in step 1 (supersedes the bullets above where they differ):

- `GetSubscriptionsAsync(installRoot, extraGameArgs)` → `Result<WorkshopSubscriptions>` (items + warnings: an
  unreadable acf or collection read is a warning, not a failure); offline only. `WorkshopItem` carries `Mods` as
  `InstalledMod` (names, `incompatible` for R7), `Collections` (computed in Core), `Installed`, `Latest`, `State`.
- State order: DisabledLocally → NotInstalled → NeedsUpdate (vs fetched Web API manifest, else acf `latest_manifest`)
  → UpToDate. `NotSubscribed` is set by the caller matching a collection against the subscriptions. No downloading
  state in Core; it comes from helper progress events.
- Web API details are cached 5 min (`TimeProvider`), not per session. Ids are `ulong`; manifests decimal strings.
- `ModCatalog` split into `ReadAsync` + `ReadRootAsync` so an item folder's mods are read with the shared logic.
- Error codes: `workshop.subscriptions_unreadable`, `manifest_unreadable`, `network_failed`, `response_invalid`,
  `collection_not_found`.

## Write side (Steamworks helper)

### Process model

`IWorkshopActionRunner.RunAsync(installRoot, WorkshopActionRequest, IProgress<WorkshopActionEvent>, ct)` starts the
launcher's own executable (`Environment.ProcessPath`) with `--pzlauncher-steam-helper`. `Program.Main` checks that
argument first — before `SingleInstanceGuard` and Avalonia — and calls Core's `SteamHelperHost.RunAsync`, then exits.

- Environment: `SteamAppId=108600`, `SteamGameId=108600` set in `ProcessStartInfo.Environment` (reaches native
  `getenv` on Linux too); working directory = a launcher temp folder holding `steam_appid.txt` as a fallback.
- Request: one JSON line on stdin (`action`: subscribe | unsubscribe | update; `ids`; `steamApiPath`).
- Events: JSON lines on stdout (`started`, `progress {id, bytes, total}`, `itemDone {id, ok, error?}`, `failed {code}`);
  exit code 0 when the run itself worked. Parent maps events to `IProgress`, kills the helper on cancel or when no
  event arrives for a stall timeout.
- One helper at a time (runner-level gate); Steam "playing PZ" lasts only while it runs.

### Binding

`SteamApi` — `NativeLibrary.Load` of the install's `steam_api64.dll` (Windows: install root) / `libsteam_api.so`
(Linux: `<gameFolder>` then `<gameFolder>/natives`), exports bound with `NativeLibrary.GetExport` to unmanaged function
pointers (AOT-safe, no reflection). Exports: `SteamAPI_InitFlat`, `SteamAPI_Shutdown`, `SteamAPI_ManualDispatch_*`,
`SteamAPI_GetHSteamPipe`, `SteamAPI_SteamUGC_v021`, `SteamAPI_ISteamUGC_SubscribeItem / UnsubscribeItem / DownloadItem
/ GetItemState / GetItemDownloadInfo / GetItemInstallInfo`, `SteamAPI_ISteamUtils_*` for call results as needed.
Callback structs declared per OS packing (Windows pack 8, Linux pack 4). Manual dispatch loop: `RunFrame` + drain
`GetNextCallback`, resolve call results by `SteamAPICall_t`.

### Actions

- subscribe / unsubscribe: `SubscribeItem` / `UnsubscribeItem` per id, await `RemoteStorageSubscribe/Unsubscribe…Result_t`.
- update (force): per id `SubscribeItem` (already subscribed — makes the client refresh the item), then
  `DownloadItem(id, highPriority: true)`; poll `GetItemDownloadInfo` for progress; done on `DownloadItemResult_t` for
  that id and `GetItemState` = installed without needs-update / downloading. The parent then re-reads the acf and
  reports an item whose manifest still differs from the Web API as not updated.

As built in step 2 (supersedes the bullets above where they differ):

- Events: `started`, `progress`, `itemDone {id}`, `itemFailed {id, reason, steamResult}`, `failed {reason, message}`, and
  `finished` (written after `SteamAPI_Shutdown`). No `finished` and no `failed` = `workshop.helper_failed`.
- Working directory: the game folder (it ships PZ's own `steam_appid.txt`), not a launcher temp folder; nothing is written.
- `IProcessRunner.StartPiped` → `IPipedProcess` (stdin text, stdout lines, kill); stderr is drained as before and logged
  at Debug only.
- Concurrency: a second `RunAsync` waits for the running helper (no Conflict error).
- Stall timeout 3 min (> the helper's own 2 min per-item stall), `TimeProvider`-based; cancel kills the helper and rethrows.
- macOS has no known `steam_api` location → `workshop.steam_api_missing`.
- Error codes: `workshop.steam_api_missing`, `steam_api_unusable`, `steam_not_running`, `steam_init_failed`
  (also Steam client older than the library), `helper_start_failed`, `helper_failed`, `helper_timed_out`.

## Desktop

- `ModsSection` gains `WorkshopSubscriptions`, `WorkshopCollections`; `ModsViewModel` hosts
  `WorkshopSubscriptionsViewModel` and `WorkshopCollectionViewModel` like the existing two sections
  (desktop/shell-and-theming.md page sidebars).
- Write commands bind `CanExecute` to `!GameRunState.IsGameRunning` and no action in flight.
- Dialogs (`IDialogService` additions): Workshop progress dialog (per-item rows, cancel), three-choice pre-launch
  dialog, multi-mod pick dialog (R7). Existing `ConfirmAsync` for unsubscribe confirmations; existing name prompt
  for 生成本地合集.
- Pre-launch check: `WorkshopLaunchCheck` called in `HomeViewModel.LaunchAsync` after the order flush and before the
  collection apply. Mod ids → items via `ModCatalog` (`Source == SteamWorkshop`). Short overall timeout; any failure
  logs and returns "nothing to do".
- Remembered Steam collections: new `preferences.json` field (desktop/preferences.md contract) — list of
  `{ id, title }`, most recent first.

As built in step 3 (supersedes the bullets above where they differ):

- The step-4 view model is `WorkshopCollectionsViewModel` (plural, an empty placeholder until step 4).
- The 工坊订阅 section loads when it is shown (page activation with that section, or switching to it) and on the page's
  Refresh; the page's own load bar and error belong to the two game-file sections (`ModsViewModel.ShowsGameFiles`).
- Titles: each load asks `CheckUpdatesAsync` for the items without `Latest` (Core's 5-min cache keeps repeats offline)
  and reads again when any came back; a failed lookup is a warning under the list. 检查更新 asks for every installed
  item, reloads and shows a count.
- Link parsing is Core's public `WorkshopLinks` (bare id, steamcommunity.com `id=` link with or without scheme,
  `steam://url/CommunityFilePage/<id>`), which also builds the client page URI opened through `IUrlLauncher`.
- `IDialogService.ShowWorkshopActionAsync(WorkshopActionViewModel)`: per-item rows (state, percent, failure,
  Steam EResult), cancel closes and cancels the run, the end shows a summary with Close. After an update, items
  Steam reported done but whose re-read state is still `NeedsUpdate` are shown as still outdated. Download progress
  also shows on the list rows as 下载中. The list reloads after every action.
- Write actions (subscribe, unsubscribe, update all, force update selected) are disabled while a launcher-started game
  runs or an action is open; 检查更新 and open-in-Steam stay available.

As built in step 4 (supersedes the bullets above where they differ):

- 工坊合集: link/id box (same `WorkshopLinks` parsing) and the remembered list on the left, the open collection on the
  right. Opening calls `GetCollectionAsync` (which also caches the children's details), then `GetSubscriptionsAsync`
  and merges: a subscribed item keeps its `WorkshopItem`, any other becomes one with `NotSubscribed` and the collection's
  details. Showing the section again re-merges without the Web API; the page's Refresh opens the collection again.
  A failed open clears the view and shows the error; it isn't remembered.
- Remembered collections: `WorkshopCollectionHistory` (`Services/Mods`, concrete like `GameRunState`) over the new
  `workshopCollections` preference (`SavedWorkshopCollection`: id as decimal text, title optional). Opening moves the
  collection to the top with its current title; entries are removable.
- The step-3 action plumbing (progress dialog, row download state, still-outdated re-read, unsubscribe confirmation,
  open-in-Steam) moved to `WorkshopActionFlow`, shared by both sections. Write actions disable as in step 3.
- 生成本地合集: items not subscribed, not installed or without mods are skipped; when any item holds several mods or
  any is skipped, one `IDialogService.PickWorkshopModsAsync(WorkshopModPickViewModel)` dialog shows a checkbox per mod
  of the multi-mod items (incompatible pairs among all candidate mods marked, either direction) and the skipped items
  with their reason. Then the shared name prompt (`ModCollectionPrompts.NamePrompt` + `AskNameAsync`, prefilled with
  the Steam title, overwrite confirm) and `ModsViewModel.WriteAsync` save; the new collection is selected in 合集管理.
  Nothing to add shows an error before any prompt.

As built in step 5 (supersedes the bullets above where they differ):

- `WorkshopLaunchCheck` is a concrete Desktop service (`Services/Mods`, like `ModCollectionApplier`): the "never blocks the
  launch" policy (any failure or a 10 s `TimeProvider` timeout logs and returns nothing) belongs to the launch flow, and the
  rest only composes Core's public API.
- Mods the launch enables: the current collection resolved with `ModLoadOrder.Resolve` (requirements included); with no
  current collection, or one gone from the file, `default.txt`'s enabled list. Items: subscribed, installed and not
  disabled locally, whose `Mods` contain one of those ids (not only the first copy of an id the game loads). Outdated:
  installed manifest ≠ the Web API's; an item the Web API doesn't show keeps Steam's own `NeedsUpdate`.
- `HomeViewModel.LaunchAsync` runs it after the order flush and before the apply. Three-choice dialog:
  `IDialogService.ChooseAsync(ConfirmPrompt, alternativeText)` → `ConfirmChoice` (confirm 更新后启动, alternative 直接启动,
  cancel 取消), items listed by title (id). 更新后启动 runs `WorkshopActionFlow.RunAsync` (the step-3 progress dialog); any
  item not `Done` afterwards (failed, still outdated, run error, dialog closed early) asks 直接启动 / 取消 with
  `ConfirmAsync`, naming those items.

## Compatibility and risk

- No change to existing files' formats; one new preferences field (absent = empty list).
- Spike gate (PRD D6) covers the untested pieces: AOT + PZ's DLL init, overlay, Linux library path.
- Steam account mismatch (helper acts as whoever Steam is signed in as; local files read the same `SteamLoginUsers`
  account) — documented, not handled further.
- Rollback: the feature is additive; removing the two sections and the Home hook restores today's behaviour.
