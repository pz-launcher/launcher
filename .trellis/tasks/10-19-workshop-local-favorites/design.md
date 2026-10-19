# Design — local Workshop favorites

Desktop only; no Core or helper change (titles come from existing `IWorkshopService.CheckUpdatesAsync` /
`GetCollectionAsync` / `GetSubscriptionsAsync`).

## Storage

- New preferences field (desktop/preferences.md contract), e.g. `workshopFavorites`:
  `{ collections: [{id, title}], items: [{id, title}] }`, ids as decimal strings, newest first, title omitted when
  unknown; read robustly like `workshopCollections` (bad entries dropped, bad shape → empty).
- `WorkshopFavorites` service (Services/Mods, beside `WorkshopCollectionHistory`): `IsFavorite(kind, id)`,
  `Toggle(kind, id, title)`, `UpdateTitle`, `Items`/`Collections`, a `Changed` event; fire-and-forget save. Reuse
  `WorkshopCollectionHistory`'s save/shape code where it is the same (extract shared bits rather than copy).

## View models

- `WorkshopItemRow` gains `IsFavorite` + a toggle command routed to `WorkshopFavorites` (rows are rebuilt on reload, so
  the row reads the service; the service's `Changed` keeps rows in sync across sections).
- 工坊订阅: star column; filter `WorkshopSubscriptionsFilter.Favorites`.
- 工坊合集:
  - left panel order: 本地收藏的物品 (fixed entry) / 本地收藏 (collections) / 收藏的合集 / 我的合集 / 最近打开; star toggles on
    the Steam/recent rows and on the opened collection header.
  - "本地收藏的物品" opens a pseudo-collection: ids from `WorkshopFavorites.Items`, details from `CheckUpdatesAsync`,
    merged with `GetSubscriptionsAsync` exactly as an opened collection merges (reuse that merge path), title text
    "本地收藏的物品", no 取消订阅全部 and no 在 Steam 中打开 for the whole view; 生成本地合集 prefills "本地收藏".
  - unstarring an item while that view is open removes its row without a reload.
- Gating: toggles are local writes, allowed while a game runs; Workshop actions keep their existing gating.

## Share and import

- Text format owned by one small class (e.g. `WorkshopFavoritesShareText` in Desktop Services/Mods): `Format` and
  `Parse`, markers `#collections` / `#items` as constants; parsing reuses `WorkshopLinks.TryParseItemId` per line.
- Unmarked ids are classified with `IWorkshopService.GetCollectionAsync` (success → collection, `collection_not_found`
  → item); network failure → those ids are reported as skipped, nothing guessed.
- 分享 uses the existing `IClipboardService`; 导入 uses the existing text prompt (multi-line if the prompt supports it,
  else extend it minimally). Buttons sit by the 本地收藏 group.

## Risks

- Section VMs are app-lifetime; the `Changed` event subscription lives for the app (same as existing singletons).
- Rollback: additive; remove the field, service, column and entries.

## As built

- Storage: `SavedWorkshopCollection` / `WorkshopCollectionFile` renamed to `SavedWorkshopEntry` / `WorkshopEntryFile` (one `{id, title}`
  shape for history and favorites); the store's entry-list read/write (`ToWorkshopEntries`, `ToElement`) is shared by `workshopCollections`
  and `workshopFavorites`. `UserPreferences.WorkshopFavorites` is non-null (`SavedWorkshopFavorites.None`), like the other lists.
- Service: `WorkshopEntryList` (internal) holds the most-recent-first id list for both `WorkshopCollectionHistory` and `WorkshopFavorites`.
  `WorkshopFavorites` API: `IsFavorite`, `Toggle`, `UpdateTitles` (batch, saves once), `Add` (import merge, returns counts), `Changed`.
- Rows: the star is a section command (`ToggleFavoriteCommand` on `WorkshopSectionViewModel`, row as parameter, like `OpenInSteamCommand`);
  `WorkshopItemRow.IsFavorite` is pushed by the sections on `Changed`. Collection rows reuse `RememberedWorkshopCollectionRow` (本地收藏 group)
  and gain `IsFavorite` (default false) on it and `WorkshopUserCollectionRow`.
- 本地收藏的物品: a stand-in `WorkshopCollection` with id 0 and `IsFavoriteItemsView`; it goes through the same `MergeAsync` / `ItemOf` path.
  Section re-show and refresh reload it (`CheckUpdatesAsync`, cached by Core for a few minutes) so items starred elsewhere appear; a failed
  title lookup lists the items by id with a warning. 在 Steam 中打开 (collection) and 取消订阅全部 are hidden and disabled there.
- Titles refresh from: an opened collection, the Steam favorited/published lists, any item rows built in either section, and after import.
- Import: the fixed `#collections` / `#items` markers match ignoring case; an id repeated in its list counts once and is not "skipped";
  blank lines aren't counted. Unmarked ids not starred yet are classified one by one with `GetCollectionAsync`. Text without an id is rejected in the
  prompt (accept disabled with the reason), as in the mod collection import. Share / import buttons are icon buttons beside the 本地收藏 header;
  their status is a hint line under it.
- Star look: `ic:FluentIcon.favoriteStar` inside a `Button` with `Classes.favorite` (style in `ShellStyles.axaml`), outline vs filled accent.
