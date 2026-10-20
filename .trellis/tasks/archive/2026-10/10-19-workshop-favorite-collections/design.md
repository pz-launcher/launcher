# Design — favorited and own Steam collections

## Core

- **Helper** (`Steam/Workshop/Helper/`): new request action for listing the user's collections. The helper's request
  uses `WorkshopAction` today; add a member (e.g. `ListCollections`) or a separate request kind — whichever keeps the
  write-action paths and wire format of existing actions unchanged (SteamHelperProtocolTests pin them).
  - Session: account id from `SteamAPI_SteamUser` → `GetSteamID` (lower 32 bits). For each list (Favorited, Published):
    page 1.. → `CreateQueryUserUGCRequest(account, list, k_EUGCMatchingUGCType_Collections, CreationOrderDesc, 0,
    108600, page)`, `SetReturnChildren(true)`, `SendQueryUGCRequest` → call result 3401 → `GetQueryUGCResult(i)` per
    index → `ReleaseQueryUGCRequest`; stop when a page returns fewer than 50 or total reached.
  - Read `SteamUGCDetails_t` fields: publishedFileId, result, fileType, title (UTF-8, NUL-terminated, decoded from raw
    bytes), numChildren — offsets per packing, like the existing payload readers. Verify enum values, callback id,
    struct sizes against the SDK (spike notes in `research/favorite-collections.md`).
  - New helper event carrying the result (account id + two lists of `{id, title, childCount}`), then `finished`.
- **Runner** (`Steam/Workshop/Actions/`): a new method on `IWorkshopActionRunner`, e.g.
  `ListCollectionsAsync(installRoot, ct) → Result<WorkshopUserCollections>`, sharing the existing process start,
  gate (one helper at a time), stall timeout, failure mapping (`steam_not_running`, `steam_init_failed`, …) and
  stderr handling. Public DTO `WorkshopUserCollections(uint AccountId, IReadOnlyList<WorkshopCollectionSummary>
  Favorited, … Published)` in `CoreJsonContext`.

## Desktop

- `WorkshopCollectionsViewModel`: two new row lists (reuse `RememberedWorkshopCollectionRow`-style rows or a sibling
  row type with title + count), a `RefreshCollectionsCommand`, status text (last updated / failure reason via
  `ErrorMessages`), an "already fetched this session" flag. `ShowAsync` shows the saved lists, then auto-fetches once
  per session when allowed (R3). Clicking a row calls the existing open-collection path.
- Gating: `GameRunState.IsGameRunning` and the section base's `IsActionRunning`; while fetching, Workshop write
  actions wait on the runner gate as today (runner serialises helpers).
- Persistence: new preference field (desktop/preferences.md contract), e.g. `workshopUserCollections`:
  `{ accountId, fetchedAt, favorited: [{id, title, childCount}], published: [...] }`, ids as decimal strings like
  `workshopCollections`. Shown only when its accountId equals the current account (`SteamLoginUsers` current account
  — expose it through an existing Core service rather than reading files in Desktop).
- View: groups 收藏的合集 / 我的合集 / 最近打开 in the left panel, 刷新 button and status line; reuse the shared
  Shell styles.

## Risks

- Paging and Linux pack-4 offsets untested live; unit tests cover the decoding at both packings.
- The query makes Steam show the user playing PZ for a few seconds (accepted, D1/D2).
- Rollback: additive; removing the groups and the helper action restores today's section.

## As built (Core)

- Helper request: `SteamHelperRequest.Action` is now the internal `SteamHelperAction` (`Subscribe`, `Unsubscribe`, `Update`,
  `ListCollections`) instead of the public `WorkshopAction`, so the public enum gains no read-only member. Existing actions
  keep their wire names; the runner maps `WorkshopAction` with an exhaustive switch. `ListCollections` sends `"ids":[]`.
- Helper event `collections` (`SteamHelperCollections`) carries the public `WorkshopUserCollections`; then `finished`.
  A query failure is `failed` with the new `SteamHelperFailure.QueryFailed` (message: list, page, EResult or "no answer").
- Paging (`SteamHelperSession.ReadAllPages`, over an injected page query): pages from 1 until fewer than 50 returned or
  Steam's total reached, bounded at 100 pages; keeps items with EResult OK and file type collection, each id once; an
  empty title becomes `null`.
- `SteamApi`: query exports and `SteamAPI_SteamUser_v023` / `ISteamUser_GetSteamID`, bound at load like the others (all
  verified in PZ's `steam_api64.dll` export table). `SetReturnChildren(true)` is set inside `CreateUserCollectionsQuery`.
  `SteamUGCDetails_t` is read into a 9784-byte buffer: title at 24 (129 bytes, UTF-8 to the first NUL), child count at
  9768 (pack 8) / 9760 (pack 4). `SteamUGCQueryCompleted_t` (3401) has the same offsets under both packings.
- Runner: `ListCollectionsAsync(installRoot, ct)`; `RunAsync` and it share `RunHelperAsync` (steam_api lookup, own
  executable, gate, start, stall timeout, `failed`/`finished` handling, stderr logging). A run that finishes without a
  `collections` event is `workshop.helper_failed`.
- New error `workshop.collections_query_failed` (`WorkshopCollectionsQueryFailed`, External, `reason` details), with its
  `Error_` text in all 10 resx files and its `ErrorMessages` arm.
- Current account for Desktop: `IWorkshopService.GetCurrentAccountAsync(ct) → Result<SteamAccount>` (loginusers.vdf, same
  lookup as subscriptions). The list's own `AccountId` comes from `GetSteamID` in the helper.
- Desktop compile fixes only: `FakeWorkshopService.GetCurrentAccountAsync` (`Account`, `AccountError`),
  `FakeWorkshopActionRunner.ListCollectionsAsync` (`Collections`, `ListError`, `ListRuns`), DesignData samples.

## As built (Desktop)

- Preference `workshopUserCollections` (`UserPreferences.WorkshopUserCollections`, `SavedWorkshopUserCollections(FetchedAt,
  WorkshopUserCollections)` reusing the Core DTO): `{accountId, fetchedAt, favorited, published}`, list entries
  `{id, title, childCount}` with ids as decimal text; contract row in `desktop/preferences.md`. A missing account/time/list
  or wrong shape drops the whole entry; bad list entries are dropped one by one.
- `Services/Mods/WorkshopUserCollectionsCache` (singleton beside `WorkshopCollectionHistory`): `For(accountId)` and
  `Save(collections)`, which stamps `TimeProvider.GetUtcNow()` and saves fire-and-forget. `ModsViewModel` takes it and
  passes it to `WorkshopCollectionsViewModel`.
- `WorkshopCollectionsViewModel`: `Favorited` / `Published` rows (`WorkshopUserCollectionRow`: id, title or id, child
  count, open), `IsFavoritedEmpty` / `IsPublishedEmpty` (only once a result is shown), `IsFetchingUserCollections`,
  `UserCollectionsUpdatedText` (hint) and `UserCollectionsErrorText` (warning) as two lines, so a failure still shows how
  old the kept lists are. `ShowAsync` = user collections + the open collection's states; until the helper has run once
  this session it re-reads the current account (saved lists shown only for that account, none when unreadable) and
  fetches when allowed. A fetch skipped by gating doesn't use up the session's fetch; a failed one does (刷新 retries), except `workshop.steam_not_running`: Steam was never reached, so the next show asks again (PRD R3).
  `RefreshUserCollectionsCommand` is disabled without a game install, while a launcher-started game runs, while this
  section's action runs, or while a fetch is in flight.
- Gating uses this section's `IsActionRunning` only: the subscriptions section's actions run behind a modal dialog, and
  the runner gate serialises helpers anyway.
- `WorkshopSectionViewModel` exposes the runner as protected `Runner`.
- View: 收藏的合集 header with the 刷新 button (reuses `Mods_Refresh`, tooltip `WorkshopCollections_RefreshHint`), progress
  bar, rows, 我的合集, then the status lines, then 打开过的合集; the left panel scrolls as one list.
- New resx keys (all 10 files): `WorkshopCollections_Favorited`, `_FavoritedEmpty`, `_Published`, `_PublishedEmpty`,
  `_RefreshHint`, `_UpdatedFormat`.
- Test support: `TestWorkshop.UserCollections(...)`, `FakeWorkshopActionRunner.ListGate`,
  `FakePreferenceStore.SavedWorkshopUserCollections`.
