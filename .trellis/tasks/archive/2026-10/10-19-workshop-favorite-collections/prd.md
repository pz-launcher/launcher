# Favorited and own Steam collections

## Goal

In "模组 → 工坊合集", list the signed-in user's favorited Steam Workshop collections and the collections they
published for Project Zomboid, so they can be opened with one click instead of pasting a link.

User request (verbatim): 工坊合集内能看到收藏的合集

Branch: `feat/workshop-management` (PR #15); builds on archived task `10-18-workshop-management`.

## Background

- 工坊合集 today (`WorkshopCollectionsViewModel`): open a collection by pasted link/id, plus the launcher's
  "recently opened" list (`WorkshopCollectionHistory`, preference `workshopCollections`). Listing the user's own or
  favorited collections was out of scope in the archived task (its D2).
- Steamworks query spike on the user's account (`research/favorite-collections.md`):
  `CreateQueryUserUGCRequest(account, list, Collections, CreationOrderDesc, creator 0, consumer 108600, page)` with
  list = Favorited returned the 2 favorited collections, list = Published the 1 own collection. Creator must be 0
  (website-made collections have creator 766, the user's own 108600). `SetReturnChildren(true)` is needed for child
  counts. ~300 ms per query, 0.7–2.6 s with init/shutdown; Steam shows the user playing PZ while the helper runs and
  ~2 s after. All exports exist in PZ's `steam_api64.dll`; callback 3401 (280 B), `SteamUGCDetails_t` 9784 B at
  pack 8. Untested: paging past page 1, Linux pack 4, game running, Steam closed.
- Other routes considered: public profile page scraping (works only for public profiles, undocumented HTML) and web
  session cookies as `fpsacha/zomboid-control-panel` does for its own-collection sync
  (`research/zomboid-control-panel.md`; it does not list favorites). Not used (D1).

## Requirements

- **R1 Lists** — 工坊合集's left panel shows two groups above "最近打开": 收藏的合集 and 我的合集. Each row shows the
  collection title (item id when unknown) and its item count; clicking it opens the collection exactly as a
  remembered or pasted one does (it is then also added to 最近打开). A group with no collections shows a short empty
  text.
- **R2 Fetch (D1)** — the lists come from a new read-only action of the existing Steam helper process: the Favorited
  and Published queries above, all pages (50 per page), titles and child counts from the query results. Only query
  calls; nothing is subscribed, favorited or downloaded.
- **R3 Refresh and cache (D2)** — the first time 工坊合集 is shown in a launcher session the lists are fetched
  automatically; a 刷新 button next to the groups fetches again. The last successful result is saved with the Steam
  account it belongs to and shown at once on open (only for the same account), with a "上次更新" time. No automatic
  fetch while Steam isn't running, a launcher-started game runs or a Workshop action runs; the refresh button is
  disabled while a launcher-started game runs or any Workshop action runs.
- **R4 Failures** — a failed fetch (Steam not running, helper or init failure, timeout) keeps the saved lists and shows
  the reason as a status line under the groups, never a dialog. A later success clears it.
- **R5 Localization** — every new UI string in all `Lang/UiText*.resx` files.

## Acceptance Criteria

- [ ] AC1 (R1, R2) On the user's machine with Steam running, opening 工坊合集 lists the 2 favorited collections
      (3623026433, 2690796635) under 收藏的合集 and 3815132834 under 我的合集, with titles and item counts (201 / 71 /
      214 from the spike); clicking one opens it and adds it to 最近打开.
- [ ] AC2 (R3) Leaving and re-entering the section in the same session doesn't query again; 刷新 does. After a
      launcher restart the saved lists show immediately with the last-updated time, then refresh once.
- [ ] AC3 (R3, R4) With Steam closed, the section shows the saved lists and a status line saying Steam isn't
      running; no dialog. While a launcher-started game runs, 刷新 is disabled and nothing is queried.
- [ ] AC4 (R3) A saved result from another Steam account is not shown.
- [ ] AC5 (R2) Core tests cover paging (≥ 2 pages), both lists, title/child-count decoding at both packings, and
      helper failure mapping; Desktop tests cover auto-fetch once per session, refresh, cache by account, gating,
      failure status.
- [ ] AC6 (R5) Localization resource tests pass with the new keys in every resx.

## Decisions

- D1 Route (user, 2026-10-19): Steamworks query in the existing helper process (new read-only action); no profile
  page scraping, no web session cookies.
- D2 Refresh (user, 2026-10-19): automatic on the first show per launcher session plus a 刷新 button; last result
  saved and shown at once. No periodic refresh (considered and dropped: each query makes Steam show the user playing
  PZ for a few seconds).
- D3 Lists (user, 2026-10-19): favorited collections and the user's own published collections, as two groups.

## Out of Scope

- Favoriting / unfavoriting, creating or editing Steam collections.
- Favorited single items (non-collections).
- Profile page or cookie-based routes.
- Periodic background refresh.
