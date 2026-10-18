# Steam Workshop management

## Goal

Manage the player's Steam Workshop content for Project Zomboid (appid 108600) from the launcher: see and change
subscriptions, open a Steam Workshop collection and subscribe to it or turn it into a launcher mod collection, and make
Steam fetch Workshop updates it has not picked up yet, so the game never starts with stale mods.

User request (verbatim): 从最新的main开发功能。实现steam创意工坊的管理功能，包括订阅管理和合集管理。并且能辅助触发steam的创意工坊更新，因为它有时候不实时。

Branch: `feat/workshop-management`, cut from `origin/main` at `1be366a`.

## Background

Research notes with sources: `research/steam-workshop-integration.md`.

- **This repo** — win-x64 and linux-x64, Native AOT (`.github/workflows/release.yml`, `IsAotCompatible`); no Steamworks
  binding today; Core reads VDF/ACF with `ValveKeyValue`. `PzLauncher.Core/Steam/` finds Steam roots and libraries
  (`SteamLocator`, `SteamLibraryReader`, `WorkshopLibraries`) and the signed-in account (`SteamLoginUsers`: `MostRecent`
  user, else latest sign-in). `ModCatalog` / `ModLocator` list installed mods (staged Workshop → Steam Workshop content
  folders → `<cache>/mods`, first copy of an id wins) with `InstalledMod.WorkshopId` = item folder name. The "模组" tab
  (`ModsSection`: Mods, Collections) manages the game's local mod presets (`Lua/pz_modlist_settings.cfg`; archived task
  `10-17-mod-collections`) — these are not Steam Workshop collections. Home's launch (`HomeViewModel.LaunchAsync`):
  flush pending order → apply the current collection → ZombieBuddy step → `IGameLaunchService.LaunchAsync`.
  HTTP goes through `LauncherHttpClient` with the proxy settings.
- **Local Steam files** — `<SteamRoot>/userdata/<accountid>/ugc/108600_subscriptions.vdf` lists subscribed ids (time
  subscribed, `disabled_locally`), a cache Steam refreshes. `<library>/steamapps/workshop/appworkshop_108600.acf` has
  `WorkshopItemsInstalled` (size, timeupdated, manifest) and `WorkshopItemDetails` (`manifest`, `timeupdated`,
  `subscribedby`, `latest_manifest`, …). A stale `appworkshop_108600.acf` inside the PZ install folder must be ignored.
- **Keyless Web API** — `ISteamRemoteStorage/GetPublishedFileDetails` (title, `time_updated`, `hcontent_file` = acf
  `manifest`, `file_size`; 1000 ids per request works; ~30 min server cache) and `GetCollectionDetails` (children;
  `filetype` 2 = nested collection; `result` 9 for a non-collection). Outdated = acf `manifest` ≠ API `hcontent_file`
  (acf `latest_manifest` lags Steam's own view by 20–35 min). No Web API path to subscribe, unsubscribe or list
  subscriptions without a publisher key or the user's web session.
- **Steamworks (ISteamUGC v021)** — the only route for subscribe / unsubscribe / force update. `DownloadItem` alone
  does not fix an item Steam hasn't noticed is stale; one third-party report found `SubscribeItem` on an
  already-subscribed item then `DownloadItem(highPriority)` makes Steam fetch the latest (not in Valve docs). While
  initialised as 108600: Steam shows the user playing PZ, Verify Files is blocked, a deferred game update may start,
  unsubscribed items are removed only after shutdown. Needs Steam running, PZ owned, same OS user and elevation. The PZ
  install ships `steam_api64.dll` (older SDK; has `SteamAPI_InitFlat`, `SteamAPI_ManualDispatch_*`, UGC v021);
  loading it at runtime avoids redistributing Valve binaries (repo is GPL-3.0) and SDK version mismatch. Untested:
  `SteamAPI_InitFlat` from a Native AOT process against PZ's DLL, overlay injection, Linux `libsteam_api.so` location
  (expected in `<gameFolder>` or `<gameFolder>/natives`), `SteamAppId` env propagation on Linux.
- Not used: deleting acf entries or item folders, `steam://validate/108600` (would undo the launcher's
  `ProjectZomboid64.json` edits).

## Requirements

Workshop content is that of the Steam account Steam signs in with (`SteamLoginUsers`) and the Steam libraries of the
install selected in the launcher (game installs only).

- **R1 Placement (D4)** — the "模组" tab sidebar becomes 模组管理 / 合集管理 / 工坊订阅 / 工坊合集.
- **R2 Subscription list** — 工坊订阅 lists every subscribed item: title (Web API; item id when unavailable), item id,
  the mod ids it holds (installed copy), size, last updated, state (已是最新 / 需要更新 / 下载中 / 未安装 / 本地已禁用),
  and the launcher mod collections that use its mods. Search by title / item id / mod id; filters all / needs update /
  used by no collection. Refresh re-reads the local files.
- **R3 Subscribe / unsubscribe (D1)** — subscribe by pasted item link or id; unsubscribe the selected items after a
  confirmation that names the launcher mod collections using their mods; open an item's page in the Steam client.
- **R4 Update check and force update (D1, D3)** — 检查更新 compares every subscribed installed item with the Web API;
  更新全部过期项 and 强制更新所选 make Steam fetch the latest version (re-subscribe + high-priority download) and show
  per-item progress until the files on disk match the latest version, or a failure per item.
- **R5 Pre-launch check (D3)** — on a launch from the launcher, before the current collection is applied, the Workshop
  items behind the mods that launch will enable (the current collection's ids, else `default.txt`'s enabled ids) are
  checked. When any is outdated, a dialog lists them with 更新后启动 / 直接启动 / 取消: 更新后启动 runs R4 for those
  items, waits with progress, then launches (a failed item is reported and the user picks 直接启动 / 取消); 取消 aborts
  the launch. No dialog when nothing is outdated. A check that fails (network, timeout, Steam files unreadable) is
  logged and the launch goes on as today.
- **R6 Steam collection (D2)** — 工坊合集 opens a collection from a pasted link or id and lists its items in
  collection order (nested collections expanded depth-first, each item once) with title and the R2 state, including
  未订阅. Actions: 订阅缺失项 (subscribes every unsubscribed item), 取消订阅全部 (confirm, as R3), 更新过期项 (R4),
  open in Steam. Collections opened before are remembered per launcher (title + id) and can be removed from that list.
- **R7 Collection → launcher mod collection (D2, D5)** — 生成本地合集 creates a launcher mod collection (archived
  task's rules: name prompt prefilled with the Steam collection title, clash asks before overwriting) from the
  installed items in collection order. Items with one mod id add it directly; items with several mod ids are shown in
  one dialog with a checkbox per mod (all checked, `incompatible` pairs marked) before saving. Unsubscribed or not
  installed items are skipped and named in that dialog.
- **R8 Steam actions runner (D1)** — every write action (R3, R4, R5, R6) runs in a short-lived helper process that
  loads the selected install's own `steam_api`; nothing Steam-related is shipped with the launcher. When Steam isn't
  running, the DLL isn't found, or init fails, the action reports a clear error and read-only features keep working.
  Write actions are disabled while a game started by the launcher runs.
- **R9 Localization** — every new UI and error string exists in all `Lang/UiText*.resx` files.

## Acceptance Criteria

- [ ] AC1 (R1) The "模组" tab shows the four sections; switching keeps each section's state as the existing two do.
- [ ] AC2 (R2) On the user's machine the subscription list shows the same 214 items Steam lists, with titles, mod ids
      and collection usage; search and the three filters narrow it.
- [ ] AC3 (R3) Subscribing by link and by id adds the item (it appears after Steam installs it and a refresh);
      unsubscribing removes it from `108600_subscriptions.vdf` after the helper exits; the confirmation names using
      collections; open-in-Steam opens the item page.
- [ ] AC4 (R4) With an item whose acf `manifest` differs from the API's `hcontent_file`, 检查更新 marks it 需要更新 and
      更新全部过期项 brings the acf `manifest` to the API value, with progress shown; a forced update of an up-to-date
      item completes without error.
- [ ] AC5 (R5) With an outdated item among the mods to enable, launching shows the dialog; 更新后启动 updates then
      launches, 直接启动 launches at once, 取消 doesn't launch. With nothing outdated, or with the network off, the
      launch shows no dialog and starts as today.
- [ ] AC6 (R6) Pasting a collection link lists its items (a nested collection expanded); 订阅缺失项 subscribes exactly the
      unsubscribed ones; the collection is in the remembered list after restart; a non-collection id shows an error.
- [ ] AC7 (R7) 生成本地合集 creates a launcher collection whose ids follow collection order; a multi-mod item shows its
      mods with checkboxes, unchecked ones are left out; skipped items are named; the game's Mods screen shows it.
- [ ] AC8 (R8) With Steam closed, a write action reports that Steam isn't running and the list still shows; while a
      launcher-started game runs, write actions are disabled; no Valve binary is in the publish output.
- [ ] AC9 (R9) Localization resource tests pass with the new keys in every resx.

## Decisions

- D1 Integration route (user, 2026-10-18): reads from local Steam files plus the keyless Web API; subscribe,
  unsubscribe, subscribe-all and force update in a short-lived helper process loading PZ's own `steam_api`. The user
  accepts Steam showing them as playing PZ while an action runs.
- D2 Steam collection scope (user, 2026-10-18): open by pasted link/id, view with state, subscribe missing,
  unsubscribe all, turn into a launcher mod collection, remember opened collections.
- D3 Update timing (user, 2026-10-18): manual actions plus a pre-launch check with 更新后启动 / 直接启动 / 取消; a
  failed check never blocks the launch.
- D4 Placement (user, 2026-10-18): two new sections in the "模组" tab.
- D5 Multi-mod items (user, 2026-10-18): one checkbox per mod in a dialog, all checked, conflicts marked.
- D6 Spike gate: implementation starts with a helper spike on the user's machine (init against PZ's DLL from a Native
  AOT build, list subscriptions, force-update one item). If it fails, work stops and the route is re-decided with the
  user before any UI is built.

## Out of Scope

- Listing the user's own or favourited Steam collections; creating or editing Steam collections (D2).
- Workshop browsing/search, item descriptions, preview images, ratings, comments.
- Changing `disabled_locally`, Steam download settings, or the workshop download folder.
- Deleting acf entries / item folders or `steam://validate` to force updates.
- Dedicated server installs and server `WorkshopItems=` lists.
- Accounts other than the one Steam signs in with.
