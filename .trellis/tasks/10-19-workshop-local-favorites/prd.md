# Local Workshop favorites

## Goal

Let the player star Steam Workshop collections and single Workshop items inside the launcher (stored only by the
launcher, never sent to Steam) and find them again in one place.

User request (verbatim): 再添加一个本地收藏 / 本地收藏可以分享给别人导入

Branch: `feat/workshop-management` (PR #15); builds on archived tasks `10-18-workshop-management` and
`10-19-workshop-favorite-collections`.

## Background

- 工坊订阅 (`WorkshopSubscriptionsViewModel`): subscribed items, filters All / NeedsUpdate / Unused
  (`WorkshopSubscriptionsFilter`), rows `WorkshopItemRow`.
- 工坊合集 (`WorkshopCollectionsViewModel`): left panel with 收藏的合集 / 我的合集 (Steam, read-only query) and
  最近打开 (`WorkshopCollectionHistory`, preference `workshopCollections`); right side shows an opened collection's
  items (`WorkshopItemRow`) with 订阅缺失项 / 取消订阅全部 / 更新过期项 / 生成本地合集 / 在 Steam 中打开.
- Item titles for unsubscribed ids come from the keyless `GetPublishedFileDetails` (`IWorkshopService.CheckUpdatesAsync`).

## Requirements

- **R1 Star** — a star toggle on: each row of 工坊订阅; each item row of an opened collection; the opened collection
  itself (header); each row of 收藏的合集 / 我的合集 / 最近打开. Toggling never calls Steam and works while a game runs.
- **R2 Favorite collections** — 工坊合集's left panel gets a 本地收藏 group (above 收藏的合集) listing starred collections
  (title, or id when unknown), newest first; clicking opens the collection like the other groups (and adds it to
  最近打开); unstarring removes it.
- **R3 Favorite items as a local collection (D2)** — a fixed entry "本地收藏的物品" at the top of the left panel opens a
  pseudo-collection on the right listing the starred items (subscribed or not), newest first, with the same columns
  and state as an opened collection (titles from the Web API for unsubscribed items), and the actions 订阅缺失项,
  更新过期项, 生成本地合集 (name prefilled "本地收藏"), 在 Steam 中打开 per row. 取消订阅全部 is not offered there.
  Unstarring an item while this view is open removes its row.
- **R4 Filter** — 工坊订阅 gains a filter 本地收藏 (starred items only).
- **R5 Storage** — favorites are saved by the launcher (new preferences field, ids + last known titles), not per
  Steam account, and survive restarts. Titles are refreshed when a newer one is seen.
- **R6 Share (D4)** — 分享 copies the local favorites to the clipboard as plain text: a `#collections` line followed by
  one collection id per line, then an `#items` line followed by one item id per line (ids only, no links or titles;
  the section markers are fixed ASCII so text shared from any UI language imports in any other).
- **R7 Import (D4)** — 导入 takes pasted text and merges it: ids not yet favorited are added, existing favorites are
  kept, nothing is removed. Lines under `#collections` / `#items` go to that list; ids before any marker are
  classified with the keyless Web API (a collection if `GetCollectionDetails` knows it, else an item); Workshop links
  are accepted too; blank and unrecognised lines are skipped. Titles are filled from the Web API afterwards. A status
  line reports how many collections and items were added and how many lines were skipped; text with no id adds
  nothing and says so.
- **R8 Localization** — every new string in all `Lang/UiText*.resx`.

## Acceptance Criteria

- [ ] AC1 (R1, R2) Starring the opened collection adds it to 本地收藏 with its title; clicking it there opens it;
      unstarring removes it; it survives a launcher restart.
- [ ] AC2 (R1, R3) Starring a subscribed item in 工坊订阅 and an unsubscribed item inside a collection makes both appear
      under 本地收藏的物品 with titles and correct states; 订阅缺失项 there subscribes only the unsubscribed one.
- [ ] AC3 (R4) The 本地收藏 filter shows exactly the starred subscribed items.
- [ ] AC4 (R1) Toggling a star never starts the Steam helper and is allowed while a launcher-started game runs.
- [ ] AC5 (R5) Store tests: round trip, bad entries dropped, other preferences unaffected.
- [ ] AC6 (R6, R7) 分享 then 导入 on another launcher adds the same collections and items; importing twice adds
      nothing the second time; existing favorites survive an import; bare ids without markers land in the right list.
- [ ] AC7 (R8) Localization resource tests pass.

## Decisions

- D1 Scope (user, 2026-10-19): both collections and single items can be starred locally.
- D2 Presentation (user, 2026-10-19): starred items form a local pseudo-collection in 工坊合集; 工坊订阅 gets a star
  column and a 本地收藏 filter.
- D3 Not per account (user, 2026-10-19): favorites belong to the launcher, shared by every Steam account on this
  machine.
- D4 Sharing (user, 2026-10-19): clipboard text with collections and items sections, ids only; import merges.

## Out of Scope

- Syncing local favorites to Steam favorites (or the reverse).
- Starring launcher mod collections or installed non-Workshop mods.
- Sorting or reordering favorites by hand.
- Sharing as a file or an encoded code.
