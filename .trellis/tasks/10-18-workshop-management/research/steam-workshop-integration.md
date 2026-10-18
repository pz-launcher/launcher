# Research: Steam Workshop integration for a third-party PZ launcher

- **Query**: How a third-party desktop launcher (C# / .NET 10, Avalonia, Native AOT win-x64 + linux-x64, no Steamworks
  partner status) can manage Steam Workshop content for Project Zomboid (appid 108600): Steamworks SDK route, Steam
  Web API route, local-file route, and how they compare.
- **Scope**: mixed (official Valve docs, community code and issues, live probes against the Web API, read-only
  inspection of this machine's Steam install and PZ install)
- **Date**: 2026-10-18

Evidence labels used below:
**[Doc]** official Valve documentation. **[Local]** observed on this dev machine (Windows, Steam in
`C:\Program Files (x86)\Steam`, PZ in `E:\SteamLibrary`), read-only. **[Probe]** live HTTP request made during this
research. **[Community]** third-party code, issue, or report. **[Unverified]** inferred and not tested.

---

## 0. Existing launcher code this builds on

| File | Relevance |
|---|---|
| `PzLauncher.Core/Steam/WorkshopLibraries.cs` | Candidate Steam libraries, `ContentFolder(library)` = `<lib>/steamapps/workshop/content/108600`, `ItemFolder` |
| `PzLauncher.Core/Steam/SteamLibraryReader.cs` | Reads `libraryfolders.vdf` and `appmanifest_*.acf` through `SteamKeyValues` |
| `PzLauncher.Core/Steam/SteamKeyValues.cs` | KeyValues/VDF text parser; it can parse `appworkshop_108600.acf` and `<appid>_subscriptions.vdf` (same text format) |
| `PzLauncher.Core/Steam/SteamLoginUsers.cs`, `SteamAccount.cs` | Current account. `SteamAccount.AccountId` is the 32-bit id that names `userdata/<id>` and matches the acf `subscribedby` value |
| `PzLauncher.Core/Steam/SteamLocator.cs` | Steam roots |
| `PzLauncher.Core/Steam/SteamLaunchHookService.cs`, `SteamLaunchOptions.cs` | Optional hook: Steam's Play runs `"<launcher>" %command%`, so Steam starts the launcher as app 108600 |
| `PzLauncher.Core/Launch/GameLaunchService.cs` | The launcher starts the game by running the bundled `java` directly, not through Steam |
| `PzLauncher.Core/Mod/Collections/ModCollectionService.cs` | Launcher-local mod collections (not Steam collections) |
| `LICENSE` | GPL-3.0 (this matters for shipping proprietary `steam_api`, see 1.6) |

---

## 1. Steamworks SDK route (ISteamUGC through the flat API)

### 1.1 Initializing as appid 108600 from a process that is not the game

**How the app id is supplied** [Doc] (https://partner.steamgames.com/doc/sdk/api, https://partner.steamgames.com/doc/api/steam_api#SteamAPI_Init):
- If Steam started the process, the app id is already known. Otherwise `SteamAPI_Init` needs `steam_appid.txt`
  (contents only `108600`) in the **current working directory**. The docs describe the file as an override of the value
  Steam provides.
- The `SteamAppId` environment variable also works. Facepunch.Steamworks sets `SteamAppId` and `SteamGameId` with
  `Environment.SetEnvironmentVariable` and then calls init
  (https://github.com/Facepunch/Facepunch.Steamworks/blob/master/Facepunch.Steamworks/SteamClient.cs, `Init(uint appid)`).
  - **Linux caveat** [Unverified for .NET 10; background in https://github.com/dotnet/runtime/issues/4103 and
    https://github.com/dotnet/runtime/issues/51019]: on Unix, .NET keeps its own copy of the environment, so a managed
    `SetEnvironmentVariable` may not reach a native `getenv` inside `libsteam_api.so`. Options that do not depend on
    this: write `steam_appid.txt` into the cwd, P/Invoke libc `setenv`, or start a helper child process with
    `ProcessStartInfo.Environment["SteamAppId"]="108600"`.
- **Launched from the Steam Play hook**: Steam started the launcher as 108600, so `SteamAppId` is already in the
  environment and init should work with no file [Unverified for this launcher; this is standard Steam behaviour].

**Init fails when** [Doc] (steam_api.h `SteamAPI_Init`): Steam is not running; the app id can't be determined; the
process runs as a different OS user or elevation level than Steam; the signed-in account doesn't own a license for the
app; or the app isn't fully set up. Ownership of PZ and a running, signed-in Steam client are hard requirements.

**Side effects** (documented and observed by tools that already do this):

| Effect | Evidence |
|---|---|
| Steam shows the user as playing the app while the API is initialized. It holds the "running" state (the Play button becomes Stop). | RimSort #1836 "RimSort is detected as RimWorld by Steam". The maintainer: "known limitation of how we need to interact with Steam via the Steamworks SDK" (https://github.com/RimSort/RimSort/issues/1836) [Community] |
| While the tool holds the app id, Steam's **Verify integrity of game files** is blocked. | Same issue #1836 [Community] |
| Pressing **Stop** in Steam kills the tool process. | RimSort #1800: "When trying to stop it from Steam RimSort closes instead" (https://github.com/RimSort/RimSort/issues/1800) [Community] |
| Initializing can make Steam immediately start a deferred update of the base game. | windy10v10ai/firebase #1505, 2026-10-13: init as Dota 2 "让 Steam 立刻开始了 Dota 2 本体被推迟的更新" (https://github.com/windy10v10ai/firebase/issues/1505) [Community] |
| Playtime accrues for 108600 while initialized. | Follows from the "playing" state [Unverified, but expected] |
| `UnsubscribeItem` content is removed "after the game quits", meaning when the API owner shuts down or exits. | [Doc] ISteamUGC::UnsubscribeItem |
| `SuspendDownloads(true)` lasts until it is resumed "or when the game ends". | [Doc] ISteamUGC::SuspendDownloads |
| The Steam overlay renderer may be loaded into the initializing process, which would hook the Avalonia window. | [Unverified]. Test this; a short-lived helper process avoids it. |

**Releasing the app** [Community]: RimSort first ran each Steamworks operation in a separate `multiprocessing.Process`
that unloaded at the end. PR #1986 (closed) proposed one worker thread with `SteamAPI_Shutdown` after a 15 s idle
timeout. Its stated goal was to fix "Steam showing 'game running' whenever RimSort is open"
(https://github.com/RimSort/RimSort/pull/1986). Either way, the "running" state lasts as long as the API stays
initialized. A short-lived helper process (for example the launcher exe with a subcommand) guarantees release
because the process exits.

**Launching the game afterwards**:
- This launcher starts `java` directly (`GameLaunchService`), so Steam's "already running" state does not block the
  launch. RimSort's `SteamworksGameLaunch` does the same: it inits Steamworks, spawns the game exe directly, then
  unloads (https://github.com/RimSort/RimSort/blob/main/app/utils/steam/steamworks/wrapper.py, `SteamworksGameLaunch`).
  The game process then calls `SteamAPI_Init` itself. PZ ships `steam_appid.txt` = `108600` in its install folder
  [Local].
- Launching through Steam (`steam://rungameid/108600`, the Play button) while the tool holds 108600 would find the app
  "running". Shut the API down first [Unverified, but implied by the rows above].
- Steam workshop updates can fail while the game runs, because Windows locks jar files:
  `Update canceled: File commit failed: failed to move out file (File locked) ...\media\java\client\ISeeThem.jar`
  appears for 108600 in `Steam/logs/workshop_log.txt` [Local].

### 1.2 ISteamUGC calls relevant to subscriptions and updates

Source: https://partner.steamgames.com/doc/api/ISteamUGC [Doc]. Interface `STEAMUGC_INTERFACE_VERSION021`. Flat
accessor `SteamAPI_SteamUGC_v021`.

| Call | Notes |
|---|---|
| `GetNumSubscribedItems(bool bIncludeLocallyDisabled=false)` / `GetSubscribedItems(PublishedFileId_t*, uint32 cMax, bool bIncludeLocallyDisabled=false)` | Synchronous. Returns ids in subscription order (or the user's load order). Returns 0 on a game server. |
| `SubscribeItem(id)` → `RemoteStorageSubscribePublishedFileResult_t` | "It will be downloaded and installed as soon as possible." No batch form, so call once per item. |
| `UnsubscribeItem(id)` → `RemoteStorageUnsubscribePublishedFileResult_t` | Content is removed after the app quits (see 1.1). |
| `GetItemState(id)` → `EItemState` flags | `None 0`, `Subscribed 1`, `LegacyItem 2`, `Installed 4` ("maybe out of date"), `NeedsUpdate 8` ("not installed yet or creator updated the content"), `Downloading 16`, `DownloadPending 32` ("DownloadItem was called… content isn't available until DownloadItemResult_t") |
| `GetItemInstallInfo(id, &sizeOnDisk, folder, cch, &timestamp)` | Valid when `Installed`. **Side effect: marks the item "used" and adds it to the user's `UsedOrPlayed` list.** |
| `GetItemDownloadInfo(id, &downloaded, &total)` | Progress for items in `NeedsUpdate`. `total` is valid only after the download starts. |
| `DownloadItem(id, bHighPriority)` → `DownloadItemResult_t` (a callback, not a call result) | "Download or update a workshop item… If the workshop item has an item state of k_EItemStateNeedsUpdate, then this function can be called to initiate the update." High priority pauses other Steam downloads. Compare the callback's app id with yours, because it fires for every download. Returns false if the id is invalid or the user isn't logged on. |
| `SuspendDownloads(bool)` | Pauses all workshop downloads for the app until resumed or the app ends. |
| `SetItemsDisabledLocally` / `SetSubscriptionsLoadOrder` | Newer SDKs. Locally disabled items are excluded from `GetSubscribedItems` by default. |

**Does `DownloadItem` force a fetch of the latest version of a stale subscribed item?** It is not guaranteed.
- windy10v10ai/firebase #1505 [Community] tested this (2026-10-13, using Dota 2's own `steam_api64.dll` through
  `SteamAPI_InitFlat` + `ISteamUGC` v021). While the client's workshop "item change feed" was behind the server,
  every one of these still delivered the **old** revision:
  - `DownloadItem` alone: Steam re-verified the old files and reported OK.
  - `CreateQueryUGCDetailsRequest` + `SetAllowCachedResponse(0)` followed by `DownloadItem`: the query returned the
    new `rtimeUpdated`, but the download job still used the old revision.
  - Moving the item folder away, then `DownloadItem`: Steam re-downloaded 98 MB of the old revision.
  - In the earlier manual attempts: resubscribing in the client, the console command `workshop_download_item`, and
    clearing the download cache.
- **The workaround they found**: call `SubscribeItem` on an item that is **already subscribed**, then `DownloadItem`.
  Steam asks the server for that item's latest info (log line `Detected workshop change (latest from server)`), updates
  `latest_manifest`, and downloads the new revision. No `UnsubscribeItem` was needed. Measured: upload finished
  11:11:22; `DownloadItem` at 11:11:34 kept the old revision; `SubscribeItem` + `DownloadItem` at 11:11:45 got the new
  one.
- When Steam already knows a newer revision exists (`latest_manifest != manifest`, `NeedsUpdate` set), `DownloadItem`
  + `GetItemDownloadInfo` start the update immediately and report progress (same issue).
- [Local] PZ's own `DownloadItem` calls appear in `workshop_log.txt` as `Download item <id> requested by app` →
  `Item info for 1 item(s) (unknown only) … 1 update(s)` → `Detected workshop change (latest from server)`. So
  `DownloadItem` does refresh items that aren't in the client's cache. The stale case above affects cached items.
- RimSort's "resubscribe" sequence is: `UnsubscribeItem` → wait 1 s → `SubscribeItem` → wait 1 s →
  `DownloadItem(high=True)`. It was introduced for missing or broken mods (RimSort #1460). The wrapper notes
  "callbacks unreliable for batch operations", so it waits on a timer
  (https://github.com/RimSort/RimSort/blob/main/app/utils/steam/steamworks/wrapper.py). Unsubscribing deletes the
  content, so this is heavier than the subscribe-again variant.

### 1.3 Collections through ISteamUGC

- **Read a collection's children** [Doc]: `CreateQueryUGCDetailsRequest(ids, n)` + `SetReturnChildren(handle, true)` →
  `SendQueryUGCRequest` → `SteamUGCQueryCompleted_t` → `GetQueryUGCResult(handle, i, &SteamUGCDetails_t)`.
  `SteamUGCDetails_t.m_unNumChildren` is "the number of items in the collection if m_eFileType is
  k_EWorkshopFileTypeCollection". Then call `GetQueryUGCChildren(handle, i, ids, m_unNumChildren)`. Facepunch users
  report having fallen back to the Web API `GetCollectionDetails` for this
  (https://github.com/Facepunch/Facepunch.Steamworks/issues/586) [Community].
- **Create or edit collections** [Doc]: `CreateItem(nConsumerAppId=108600, k_EWorkshopFileTypeCollection /*=2*/)` →
  `CreateItemResult_t`. Then `AddDependency(parent, child)` → `AddUGCDependencyResult_t`: "If the
  nParentPublishedFileID item is of type k_EWorkshopFileTypeCollection, then the nChildPublishedFileID is simply added
  to that collection". `RemoveDependency` is the inverse. Title, description, preview, and visibility are set with
  `StartItemUpdate` / `SetItemTitle` / … / `SubmitItemUpdate`. Facepunch.Steamworks exposes this as
  `Ugc.Editor.NewCollection` + `Item.AddDependency()` (merged PR
  https://github.com/Facepunch/Facepunch.Steamworks/pull/446) [Community].
  - Developer/partner status is not needed: ordinary PZ players publish workshop items through this same ISteamUGC
    path. The local log shows `Create new workshop item of type Community for AppID 108600 … (OK)` [Local].
  - [Unverified for PZ]: whether creating a *collection* type from a game client is accepted for 108600, and whether
    `AddDependency` from app 108600 can edit a collection the user made on the website. Web-made PZ collections report
    `creator_app_id: 766` ("Steam Workshop") with `consumer_app_id: 108600` [Probe]. Test before relying on either.
- **List the user's own, favorited, or subscribed collections** [Doc]:
  `CreateQueryUserUGCRequest(accountId, eListType, k_EUGCMatchingUGCType_Collections /*3*/, sortOrder, nCreatorAppID, nConsumerAppID=108600, page)`
  with `k_EUserUGCList_Published 0`, `Favorited 5`, `Subscribed 6` ("Restricted to the current user only"), or
  `Followed 8`. Results come 50 per page.
  - Caveat: because web-made collections have `creator_app_id` 766, a query that pins `nCreatorAppID = 108600` may miss
    them. Facepunch forces `creatorApp = consumerApp` when unset, and that is reported to drop items whose creator app
    differs (https://github.com/Facepunch/Facepunch.Steamworks/issues/857) [Community]. Try `nCreatorAppID = 0` or `766` [Unverified].
- **Subscribe to everything in a collection**: there is no batch call. Resolve the children (recursively: a child with
  `filetype` 2 is a nested collection, see 2.1), then call `SubscribeItem` for each.

### 1.4 .NET bindings and Native AOT

| Option | AOT status | Platforms / packaging | Notes |
|---|---|---|---|
| **Steamworks.NET** (rlabrecque, MIT) https://github.com/rlabrecque/Steamworks.NET | Release 2025.164.0 (2026-07-26): "Fixed: PublishAot compliance (#692)" | NuGet `Steamworks.NET` is stuck at **2024.8.0** with no native binaries; the GitHub releases are newer. The standalone build is compiled per OS (`STEAMWORKS_WIN` vs `STEAMWORKS_LIN_OSX`) because callback struct packing differs (Windows pack 8, Linux/macOS pack 4; `Packsize.cs`). | Uses manual dispatch internally (`SteamAPI_ManualDispatch_*` in `CallbackDispatcher.cs`). `InitEx` sends the full list of interface versions from **its** SDK (1.64/1.65: `SteamUtils011`, `STEAMAPPS_INTERFACE_VERSION009`, …) to `SteamInternal_SteamAPI_Init`. |
| **Steamworks.NET.AnyCPU** (fork by Akarinnnnn, MIT) https://github.com/Akarinnnnn/Steamworks.NET.AnyCPU | Targets net8.0+. Its docs include a NativeAOT recipe ("Godot 4.7+ NativeAOT") | NuGet `Steamworks.NET.AnyCPU` **2026.165.2** (SDK 1.65). **Bundles** `runtimes/win-x64/native/steam_api64.dll`, `runtimes/linux-x64/native/libsteam_api.so`, etc. A custom `DllImportResolver` can point at another file. | One assembly for all OSes. Probably the easiest drop-in, but it ships the Valve binaries. |
| **Facepunch.Steamworks** (MIT) https://github.com/Facepunch/Facepunch.Steamworks | Not declared AOT-compatible. One PR fixes an AOT-only issue (https://github.com/Facepunch/Facepunch.Steamworks/pull/818). | NuGet **2.3.3** is old and **Win64 only** (`Facepunch.Steamworks.Win64.dll` + `content/steam_api64.dll`). Linux (`Posix`) needs a source build. | High-level async API (`Ugc.Query`, `SteamUGC.DownloadAsync`, `Editor.NewCollection`). `Init` sets the `SteamAppId` env var. |
| **Hand-written `[LibraryImport]` over `steam_api_flat.h`** | Fully under your control. Blittable structs + function pointers work with AOT. | Load at runtime with `NativeLibrary.Load(path)` / `TryGetExport`. That lets you bind to **PZ's own** DLL (1.6). | About 25 exports needed (below). Matches the approach in RimSort PR #2109 (a minimal 11-function shim replacing SteamworksPy, https://github.com/RimSort/RimSort/pull/2109) and the windy10v10ai prototype (`SteamAPI_InitFlat` + UGC v021). |

**Minimum flat surface for a hand-written binding** (all confirmed exported by PZ's `steam_api64.dll` [Local]):
- Init and shutdown: `SteamAPI_InitFlat(SteamErrMsg*)` (it checks the interface versions the DLL itself was built
  with, so it can't mismatch that DLL), `SteamAPI_Shutdown`, `SteamAPI_GetHSteamPipe`.
- Interface: `SteamAPI_SteamUGC_v021` (or `SteamAPI_SteamUser_v023` + `SteamAPI_ISteamUser_GetSteamID` for the
  account id).
- Manual dispatch [Doc] ("especially useful for binding layers", https://partner.steamgames.com/doc/sdk/api):
  `SteamAPI_ManualDispatch_Init`, `_RunFrame(pipe)`, `_GetNextCallback(pipe, CallbackMsg_t*)`,
  `_GetAPICallResult(pipe, hCall, buf, cb, expectedCallbackId, out failed)`, `_FreeLastCallback(pipe)`.
  - Call results arrive as `SteamAPICallCompleted_t` (id 703), carrying `m_hAsyncCall`, `m_iCallback`, `m_cubParam`.
    Map each `SteamAPICall_t` to a `TaskCompletionSource`. `DownloadItemResult_t` and `ItemInstalled_t` arrive as plain
    callbacks.
  - Pump on one dedicated thread.
- `SteamAPI_ISteamUGC_*`: `GetNumSubscribedItems`, `GetSubscribedItems`, `GetItemState`, `GetItemInstallInfo`,
  `GetItemDownloadInfo`, `DownloadItem`, `SubscribeItem`, `UnsubscribeItem`, `SuspendDownloads`,
  `CreateQueryUGCDetailsRequest`, `CreateQueryUserUGCRequest`, `SetReturnChildren`, `SetAllowCachedResponse`,
  `SendQueryUGCRequest`, `GetQueryUGCResult`, `GetQueryUGCChildren`, `ReleaseQueryUGCRequest`, plus `CreateItem`,
  `AddDependency`, `RemoveDependency`, `StartItemUpdate`, `SetItemTitle`, `SetItemVisibility`, `SubmitItemUpdate` if
  collection editing is in scope.
- **Struct packing**: callback and result structs use pack 8 on Windows and pack 4 on Linux
  (`VALVE_CALLBACK_PACK_LARGE`/`SMALL`). Hand-written structs need per-OS layouts or explicit offsets.
  `SteamUGCDetails_t` is large (fixed char buffers for title, description, tags, URL) [Doc + Steamworks.NET `Packsize.cs`].

### 1.5 PZ's bundled Steam runtime [Local]

- `E:\SteamLibrary\steamapps\common\ProjectZomboid\steam_api64.dll` (319,584 bytes, dated 2026-10-07) plus
  `steam_appid.txt` (`108600`).
- Exports include `SteamAPI_InitFlat`, `SteamInternal_SteamAPI_Init`, `SteamAPI_ManualDispatch_*`, 96
  `SteamAPI_ISteamUGC_*` functions (including `DownloadItem`, `GetItemState`, `SubscribeItem`, `AddDependency`,
  `GetQueryUGCChildren`), and `SteamAPI_SteamUGC_v021`.
- Interface strings: `SteamClient021`, `SteamUtils010`, `STEAMAPPS_INTERFACE_VERSION008`, `SteamUser023`,
  `SteamInput006`, `STEAMUGC_INTERFACE_VERSION021`. That is an older SDK than Steamworks.NET 1.64/1.65, which expects
  `SteamUtils011`, `STEAMAPPS_INTERFACE_VERSION009`, `SteamInput007`, `SteamClient023`.
  **Consequence**: pairing current Steamworks.NET with PZ's DLL risks missing exports (for example
  `SteamAPI_SteamUtils_v011`) or an init version mismatch. A hand-written binding that uses only UGC021 plus
  `SteamAPI_InitFlat` avoids this.
- Linux: PZ's `LD_LIBRARY_PATH` puts `<gameFolder>/natives` and `<gameFolder>` on the loader path
  (`LinuxLaunchEnvironment.cs`; the depot nests the game under `ProjectZomboid/projectzomboid/`). `libsteam_api.so` is
  expected in one of those folders [Unverified: no Linux install inspected].
- A stale `ProjectZomboid/steamapps/workshop/appworkshop_108600.acf` (2024) also exists **inside the PZ install
  folder** [Local]. Don't confuse it with the library-level `<lib>/steamapps/workshop/appworkshop_108600.acf`.

### 1.6 Redistribution and licensing

- Steamworks SDK Access Agreement
  (https://partner.steamgames.com/documentation/sdk_access_agreement) [Doc]:
  - §1.1(b) grants a license to "reproduce and distribute the part of the SDK provided inside the folder named
    redistributable_bin (the 'SDK Redistributables') along with the Licensee Software in object code form."
  - §2.3: no implied partnership or trademark use.
  - §2.4: software that talks to Steamworks Services must go "always through the application programming interface
    (API) provided by the SDK Redistributables" and must not reimplement it.
  - The agreement binds whoever downloads the SDK. It does not require partner status [Unverified that any Steam
    account can download the SDK zip, but bindings and their NuGet packages do redistribute it, see below].
- Precedents for shipping the redistributables in free or open-source tools [Community]:
  - **RimSort** (GPL-3.0, ~1.3k stars) commits `libs/steam_api64.dll`, `libsteam_api.so`, `libsteam_api.dylib` and a
    `steam_appid.txt` at the repo root, and inits as RimWorld.
  - **Steamworks.NET.AnyCPU** and **Facepunch.Steamworks** NuGet packages bundle `steam_api64.dll` /
    `libsteam_api.so`.
- **GPL-3.0 interaction** [Unverified, not legal advice]: this repo is GPL-3.0. `steam_api` is proprietary and is not a
  "System Library" in the GPL sense. Projects in this position usually add a linking exception or simply load it
  dynamically, as RimSort does. Either way it should be a conscious decision by the copyright holder.
- **Load PZ's own DLL at runtime instead of shipping one**: `NativeLibrary.Load(<PZ install>/steam_api64.dll)` (on
  Linux, `.../libsteam_api.so`). Nothing is redistributed, and the user already has the file because they own PZ.
  - Precedent: windy10v10ai launcher using Dota 2's bundled `steam_api64.dll` [Community].
  - Costs: the DLL's SDK version changes when PZ updates, so probe exports with `TryGetExport` and degrade gracefully.
    It only works for Steam installs of PZ (not manual or non-Steam copies). The process cwd still needs the app id
    (1.1).

---

## 2. Steam Web API route (no Steamworks)

### 2.1 Keyless metadata

Both methods below are `POST`, form-encoded, and need **no key**. They are the only workshop methods that
`ISteamWebAPIUtil/GetSupportedAPIList` lists without a key [Probe]. Docs:
https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage [Doc].

**`https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/`**: `itemcount=N&publishedfileids[0]=…`
- [Probe] Per-item fields returned: `publishedfileid`, `result` (1 = OK, 9 = not found or hidden; check per item),
  `creator`, `creator_app_id`, `consumer_app_id`, `filename`, `file_size` (string), `file_url`, `hcontent_file`,
  `preview_url`, `hcontent_preview`, `title`, `description` (BBCode), `time_created`, `time_updated`, `visibility`,
  `banned`, `ban_reason`, `subscriptions`, `favorited`, `lifetime_subscriptions`, `lifetime_favorited`, `views`,
  `tags[{tag}]`.
- No `file_type` and no children in v1.
- [Probe] **`hcontent_file` equals the acf `manifest`, and `time_updated` equals the acf `timeupdated`.** Item
  2409333430 gave `hcontent_file` 2026051810389825471 / `time_updated` 1791483143 from the API, and the same values in
  the acf. The linux-mod-manager spike confirms the same for other games
  (https://github.com/DonovanMods/linux-mod-manager/issues/268) [Community].
- [Probe] One request with **1000 ids** returned HTTP 200, 4.0 MB, in 4.7 s (992 OK, 8 `result: 9`). The response
  carries `Expires` 30 minutes after `Date`, so results are CDN-cached and may lag by up to about 30 min.

**`https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/`**: `collectioncount=N&publishedfileids[0]=…`
- [Probe] Per collection: `publishedfileid`, `result` (1 = collection, **9 for ids that aren't collections**), and
  `children[{publishedfileid, sortorder, filetype}]`. `filetype` 0 = item and **2 = nested collection**
  (`EWorkshopFileType`: Community 0, Microtransaction 1, Collection 2, [Doc] ISteamRemoteStorage). 31 collections in
  one request worked. Child counts seen: 16 to 852.
- Collections show `creator_app_id: 766` ("Steam Workshop", per store appdetails) in `GetPublishedFileDetails`
  [Probe]. Combined with a `GetCollectionDetails` `result == 1`, that identifies a collection.

**Rate limits**: none are documented for keyless calls. The Web API Terms of Use cap keyed use at 100,000 calls per
day and require keys to stay confidential (https://steamcommunity.com/dev/apiterms) [Doc]. The Web API overview says
403s (for example, a wrong key type) "will incur strict rate limits for the connecting IP"
(https://partner.steamgames.com/doc/webapi_overview) [Doc]. Batch ids and cache results.

### 2.2 Keyed or authenticated methods

[Probe] HTTP status without a key: `IPublishedFileService/GetDetails` 401, `GetUserFiles` 401, `QueryFiles` 403,
`Subscribe` 403. Required parameters below come from the xPaw listing (https://github.com/xPaw/SteamWebAPIDocumentation, `api.json`) [Community].

| Method | Requirement | Use |
|---|---|---|
| `IPublishedFileService/GetDetails` (GET) | `key` | Richer details: `includechildren`, `file_type`, kv tags |
| `IPublishedFileService/QueryFiles` (GET) | user key | Search and browse. `return_children`, `child_publishedfileid` ("find all items that reference the given item") [Doc] |
| `IPublishedFileService/GetUserFiles` (GET) | `key`, `steamid`, `appid` | A user's published files (`type`, `filetype` filters) [Unverified which list types a plain user key can see] |
| `IPublishedFileService/Subscribe` / `Unsubscribe` (POST) | `key` (in practice the signed-in user's `access_token` / web session) plus `publishedfileid`, `list_type`, `appid`, `notify_client`, `include_dependencies` | Subscribe on behalf of the signed-in user |
| `ISteamRemoteStorage/EnumerateUserSubscribedFiles`, `SubscribePublishedFile`, `UnsubscribePublishedFile` | **publisher key** on `partner.steam-api.com`: "can never be used directly by clients" [Doc] | Not usable |

**Answer**: there is no way to subscribe, unsubscribe, or list a user's subscriptions through the Web API without
either the game's publisher key (not available) or the user's own authenticated web session or access token (which
would mean handling Steam credentials or cookies). The linux-mod-manager spike reached the same conclusion: "Manage
subscriptions (sub/unsub): NO-GO — Requires a Steam client session". It also notes that shipping a shared Web API key
violates the ToU, so users must bring their own key for `QueryFiles` [Community].

---

## 3. Local-file route

### 3.1 `<library>/steamapps/workshop/appworkshop_108600.acf` [Local, real file, 214 items]

```
"AppWorkshop"
{
  "appid" "108600"
  "SizeOnDisk" "<bytes>"           // all installed items
  "NeedsUpdate" "0|1"              // app-level: some item needs an update
  "NeedsDownload" "0|1"            // app-level: some item needs downloading
  "TimeLastUpdated" "<unix>"
  "TimeLastFullCheck" "<unix>"     // absent in one older acf on this machine
  "TimeLastAppRan" "<unix>"
  "LastBuildID" "<app build>"
  "WorkshopItemsInstalled" { "<id>" { "size" "…" "timeupdated" "<unix>" "manifest" "<u64>" } … }
  "WorkshopItemDetails"    { "<id>" { "manifest" "<u64>" "timeupdated" "<unix>" "timetouched" "<unix>"
                                      "subscribedby" "<32-bit accountid>"
                                      "latest_timeupdated" "<unix>" "latest_manifest" "<u64>" } … }
}
```
- Other games' acfs on this machine also contain `ugchandle`, `author_snapshots`, `game_branch_min/max`, and `public`.
  Parsers should ignore unknown keys.
- **Installed revision**: `WorkshopItemsInstalled[id].manifest` / `.timeupdated`.
- **Revision Steam knows is latest**: `WorkshopItemDetails[id].latest_manifest` / `.latest_timeupdated`.
  `manifest != latest_manifest` means Steam already knows about an update it hasn't applied. The waywallen proposal
  uses exactly this signal (https://github.com/waywallen/waywallen/issues/168) [Community].
  - Limitation: `latest_*` is only as fresh as the Steam client's item feed. In windy10v10ai #1505 both `manifest` and
    `latest_manifest` stayed old for 20 to 35 minutes after a release while the Web API already showed the new one.
    **To detect staleness that Steam itself hasn't noticed, compare the acf `manifest` with the Web API
    `hcontent_file`.** `time_updated` is the secondary signal.
- **Subscribed but not installed**: an id in `WorkshopItemDetails` (or in the subscriptions file, 3.3) with no
  `WorkshopItemsInstalled` entry and/or no `content/108600/<id>` folder. App-level `NeedsDownload` is 1 in that case.
- `subscribedby`: 32-bit account id. The library acf is shared across Steam accounts on the machine, so filter by the
  current account (`SteamAccount.AccountId`).
- Snapshot on this machine: 214 items, 0 with `manifest != latest_manifest`, 1 distinct `subscribedby`. The file was
  rewritten while this research ran, so Steam updates it live. Read it without locking and expect it to change.

### 3.2 Forcing Steam to re-download or update

| Technique | Mechanism / evidence | Steam must be closed? | Assessment |
|---|---|---|---|
| `SubscribeItem` on an already-subscribed item, then `DownloadItem` (Steamworks) | Forces a "latest from server" lookup for that item. windy10v10ai #1505 (2026-10-13) [Community] | No (needs Steam running) | Most targeted method found. Needs the SDK route. |
| `DownloadItem(id, true)` alone | Works when Steam already knows about the update. Does not refresh a stale feed (#1505) | No | Good for "Steam knows but hasn't downloaded yet". |
| Unsubscribe, wait, subscribe, then `DownloadItem` (RimSort "resubscribe") | Deletes the content, then re-downloads. RimSort wrapper.py [Community] | No | Heavy, and leaves the mod missing in between. RimSort #1800 reports mods vanishing around updates. |
| Launch PZ through Steam, or restart Steam | Steam logs `Requesting updated subscription list` and `Item change feed` for 108600 each time the app runs (dozens of times per day here); `Loaded workshop items … (N installed, N needed)` [Local] | n/a | Steam's normal path. The item feed itself can lag (#1505). |
| Delete the item folder `content/108600/<id>` (and its acf entries) | Steam sees a missing item and re-downloads. In #1505 this still fetched the **old** revision while the feed was stale [Community] | **Yes**: Steam holds the acf in memory and rewrites it, and the game locks files | Re-downloads, but doesn't guarantee freshness. |
| Delete `appworkshop_108600.acf` | Widely shared community fix. Steam rebuilds the file (`Reconciling full workshop item list`, `Full item info check` appear in the log [Local]) | **Yes** | Heavy (15.9 GB of PZ workshop content here, which may be re-verified or re-downloaded). Only good as a last resort [Unverified extent]. |
| `steam://validate/108600` | "Validates the local files of an app" (Valve Developer Wiki "Steam browser protocol", archived https://web.archive.org/web/20261017231231/https://developer.valvesoftware.com/wiki/Steam_browser_protocol) | No | Validates the **game** depot. Effect on workshop content is undocumented. **It would revert the launcher's edits to `ProjectZomboid64.json`.** Not a workshop fix. |
| `steam://url/CommunityFilePage/<id>` | Opens the item or collection page inside the Steam client, where the user can Subscribe, Unsubscribe, or "Subscribe to all" for a collection (same wiki page) | No | A zero-risk fallback for subscription actions without Steamworks. Also `steam://openurl/<url>` (opens in the Steam browser). |
| `steam://exit` | Exits Steam (same wiki page) | n/a | Lets the launcher close Steam before file-based fixes. It already has restart-Steam handling for the launch hook. |
| Steam console `workshop_download_item 108600 <id>` | Manual only. Failed in the stale-feed case (#1505) | No | Not automatable. |

### 3.3 Is the subscription list available locally? **Yes** [Local]

`<SteamRoot>/userdata/<accountid>/ugc/108600_subscriptions.vdf`:
```
"subscribedfiles"
{
  "appid" "108600"
  "time_last_updated" "<unix>"
  "0" { "publishedfileid" "<id>" "time_subscribed" "<unix>" "disabled_locally" "0|1" }
  …
}
```
- On this machine there are 214 entries, matching the acf exactly (the same 214 ids). Sibling files exist for other
  apps (`<appid>_subscriptions.vdf`), plus `consumed.vdf`.
- `disabled_locally` corresponds to ISteamUGC `SetItemsDisabledLocally`.
- This is a client-side **cache**. Steam refreshes it from the server: on app runs for 108600 (observed), and other
  apps log `Skipping subscription list update. Last updated less than a day ago` [Local]. Subscriptions made on the
  website may therefore appear late. Editing this file does **not** subscribe or unsubscribe anything, because the
  server is authoritative [Unverified, but implied].
- Order: probably subscription order (newest first in this file) [Unverified].

### 3.4 Other local sources

- `<SteamRoot>/logs/workshop_log.txt` [Local] is human-readable and lists download jobs, errors (`File locked`,
  `Locking Failed`, `Access Denied`), and subscription changes. It is a useful diagnostic source but has no stable
  format.
- `content/108600/<id>/` holds the actual mod files (already used by `WorkshopLibraries.ItemFolder`).

---

## 4. Comparison

| Capability | Steamworks (ISteamUGC) | Web API, keyless | Web API with user key | Local files |
|---|---|---|---|---|
| List subscriptions | Yes (`GetSubscribedItems`) | No | No (needs session or publisher key) | **Yes** (`ugc/108600_subscriptions.vdf`, acf `subscribedby`); a cache |
| Subscribe | Yes (`SubscribeItem`) | No | No | No (`steam://url/CommunityFilePage/<id>` hands off to the user) |
| Unsubscribe | Yes (`UnsubscribeItem`, content removed when the app quits) | No | No | No (same hand-off) |
| Read collection contents | Yes (`SetReturnChildren` + `GetQueryUGCChildren`) | **Yes** (`GetCollectionDetails`, incl. nested `filetype` 2) | Yes (`GetDetails includechildren`) | No |
| Subscribe to all in a collection | Yes (loop `SubscribeItem`) | No | No | Hand-off: open the collection page; the user clicks "Subscribe to all" |
| Create or edit own Steam collection | Documented (`CreateItem` Collection + `AddDependency`/`RemoveDependency`) [Unverified for 108600] | No | No | No |
| List own / favorited collections | Yes (`CreateQueryUserUGCRequest` Published / Favorited + Collections) [watch the creator app id 766] | No | Partial (`GetUserFiles`, own key) [Unverified] | No |
| Detect outdated | `GetItemState & NeedsUpdate`, which is only as fresh as the client's feed | — | — | acf `manifest != latest_manifest` (client's view). **acf `manifest` vs keyless `hcontent_file`** (server's view) |
| Force update | **`SubscribeItem` (again) + `DownloadItem`**; `DownloadItem` when Steam already knows | No | No | Only crude options (delete folder or acf with Steam closed, relaunch); freshness not guaranteed |
| Download progress | `GetItemDownloadInfo` | No | No | Poll the folder or acf (coarse) |
| Requirements | Steam running and signed in, user owns PZ, same OS user and elevation, app id via env or `steam_appid.txt`, a `steam_api` binary (shipped or PZ's) | Internet | Internet + user-supplied key | Steam install on disk |
| Risks | Shows "playing PZ", playtime, blocks Verify, Steam's Stop kills the process, may trigger a pending game update, possible overlay injection [Unverified], interface or SDK drift if binding to PZ's DLL, proprietary binary vs GPL-3.0, struct packing per OS | Undocumented limits, ~30 min CDN cache, API can change | Key handling; never ship a shared key (ToU) | Undocumented formats; Steam rewrites files live; editing them while Steam runs is lost or unsafe |

## 5. Recommendation (as requested by the caller)

1. **Baseline, no Steamworks**:
   - Read `appworkshop_108600.acf` (installed revision, `latest_*`, `subscribedby`) and
     `userdata/<account>/ugc/108600_subscriptions.vdf` (subscription list) with the existing `SteamKeyValues` parser.
   - Use keyless `GetPublishedFileDetails` (batched, cached) for titles and previews, and compare `hcontent_file` with
     the acf `manifest` to detect outdated items.
   - Use keyless `GetCollectionDetails` (recursing on `filetype` 2) to show collection contents and their
     subscribed/installed status.
   - For actions, hand off to the Steam client with `steam://url/CommunityFilePage/<id>`.
   - All of this works on win-x64 and linux-x64 with Native AOT and no native dependency.
2. **Opt-in "Steam actions"** (subscribe, unsubscribe, subscribe to all, force update, optionally create or edit
   collections) through a **hand-written `LibraryImport` binding** to the flat API:
   - Load **PZ's own** `steam_api64.dll` / `libsteam_api.so` with `NativeLibrary.Load`, which avoids redistribution
     and GPL questions.
   - Init with `SteamAPI_InitFlat` and use only `SteamAPI_SteamUGC_v021` plus manual dispatch.
   - Run it in a **short-lived helper process** (the launcher exe with a subcommand, `SteamAppId` passed through
     `ProcessStartInfo.Environment`) so the "playing PZ" state, Steam's Stop button, and any overlay never touch the UI
     process, and release is guaranteed.
   - Force update = `SubscribeItem(id)` + `DownloadItem(id, true)`, then poll `GetItemState` /
     `GetItemDownloadInfo`, and do it when PZ is not running (Windows file locks).
   - If the binary or init is unavailable, fall back to item 1.
3. Don't use file-deletion or `appworkshop` acf deletion as automatic fixes. At most offer them as a manual,
   Steam-closed troubleshooting step with clear warnings. Don't use `steam://validate`, because it reverts
   launcher-managed game files.

## Caveats / Not found

- Not tested here (it requires initializing as 108600 on the user's account): `SteamAPI_InitFlat` against PZ's DLL from
  a .NET AOT process; the overlay behaviour; whether `CreateItem(Collection)` is accepted for 108600; whether
  `AddDependency` can edit a web-made collection (creator app 766); whether `CreateQueryUserUGCRequest` needs
  `nCreatorAppID` 0 or 766 to see web-made collections.
- `SubscribeItem`-on-subscribed refreshing a stale item comes from one detailed report (Dota 2, 2026-10-13), not from
  Valve docs.
- Linux: the location of PZ's `libsteam_api.so` and the `SteamAppId` environment propagation from .NET were not
  verified.
- No Valve documentation was found for the `appworkshop_*.acf` or `*_subscriptions.vdf` formats. The structure above is
  from real files on this machine plus community parsers.
- The Valve Developer Wiki is behind an anti-bot wall. The steam:// list was read from the Wayback snapshot (2026-10-17).
- Licensing notes are not legal advice.

---

## Spike result (step 0, 2026-10-18)

Setup: `dotnet publish PzLauncher.Desktop -r win-x64 -p:PublishAot=true -c Release`, then the published
`PzLauncher.exe --pzlauncher-steam-helper` with `SteamAppId=108600` / `SteamGameId=108600` in the environment, a working
directory without `steam_appid.txt`, one JSON request line on stdin, and
`steamApiPath = E:\SteamLibrary\steamapps\common\ProjectZomboid\steam_api64.dll`. Steam was running and signed in.
PZ (started by the installed launcher) was running for the whole spike. Code: `PzLauncher.Core/Steam/Workshop/`
(`SteamApi`, `SteamHelperSession`, `SteamHelperHost`), dispatch in `PzLauncher.Desktop/Program.cs`.

**Verdict: the route works.** Proceed with D1 as designed.

| Check | Result |
|---|---|
| Exports | Every export the binding uses exists in PZ's DLL under the expected name: `SteamAPI_InitFlat`, `SteamAPI_Shutdown`, `SteamAPI_GetHSteamPipe`, `SteamAPI_ManualDispatch_Init / RunFrame / GetNextCallback / FreeLastCallback / GetAPICallResult`, `SteamAPI_SteamUGC_v021`, `SteamAPI_ISteamUGC_SubscribeItem / UnsubscribeItem / DownloadItem / GetItemState / GetItemDownloadInfo / GetItemInstallInfo / GetNumSubscribedItems / GetSubscribedItems`. The DLL also exports `SetItemsDisabledLocally`, so its `GetNumSubscribedItems` / `GetSubscribedItems` take the trailing `bIncludeLocallyDisabled` bool. 1096 exports in total. |
| AOT | Native AOT publish has 0 IL/ILC warnings with the function-pointer binding (`NativeLibrary.Load` + `GetExport`, `delegate* unmanaged[Cdecl]`). The publish output contains no `steam_api*` file. |
| Init | `SteamAPI_InitFlat` returned OK from the AOT process. The env vars were enough, no `steam_appid.txt`. The DLL writes two lines to **stderr** (`Setting breakpad minidump AppID = 108600`, `SteamInternal_SetMinidumpSteamID: Caching Steam ID: …`), so the parent must not treat stderr output as failure. |
| `list` | 214 ids, the same set as `userdata/<account>/ugc/108600_subscriptions.vdf` (with locally disabled included). All 214 report `GetItemState` = `Subscribed, Installed`. Steam's log shows `GetDetails request … batch (0-214 of 214)` at init. Under 1 s. |
| `update`, item already current (3778421142, 6.5 KB) | Events `started`, `itemDone`; exit 0 after 2 s. acf `manifest` unchanged (`8589790132302697`, equal to the Web API `hcontent_file`); only `timetouched` moved. Steam log: `Subscribed to item …`, `Download item … requested by app`, `Item info … 0 update(s)`, `Workshop update job started while app is running, commit : yes`, `Download item … result : OK`. |
| `update`, item Steam hadn't noticed was stale (3811390574, published ~40 s earlier) | Before: acf `manifest` = `latest_manifest` = `4459181659745603925`, Web API `hcontent_file` = `987951682342026743`. After `SubscribeItem` Steam logged `Detected workshop change (latest from server) : changed cached item 3811390574, new manifest 987951682342026743`, then downloaded 38,999,824 bytes (a delta of a 253 MB item) in 6 s. The helper wrote 27 `progress` events and `itemDone`, exit 0. After: acf `manifest` = `latest_manifest` = `987951682342026743`, `size` 253378238, `timeupdated` = the API's `time_updated`. **Confirms the re-subscribe + `DownloadItem(high)` refresh from windy10v10ai #1505 for PZ.** |
| Overlay | `steamclient64.dll` and `gameoverlayrenderer64.dll` (from the Steam folder) load into the helper. The helper has no window, so nothing renders; this supports keeping Steamworks out of the UI process. |
| Process exit | The helper exits by itself after `SteamAPI_Shutdown` each time; no process left behind. The running game was unaffected. |
| "Steam stops showing PZ" | Not observable: PZ itself was running the whole time. Re-check during step 3 with the game closed. |

Packing: `CallbackMsg_t` is declared at the Windows size (24 bytes) so a pack-4 library never writes past it; payload
readers (`SteamCallbackPayloads`) pick offsets from the process OS (`HostRuntime.IsWindows`). Only the Windows layout was
exercised.

Not tested in the spike: Steam closed (`NoSteamClient` path), helper killed mid-download, `subscribe` / `unsubscribe`
actions (step 2), Linux (`libsteam_api.so` location, pack 4, env propagation).

Environment note: in this agent shell the AOT link step fails (`'vswhere.exe' 不是内部或外部命令`) because VS 18's
`vcvarsall.bat` calls `vswhere.exe` from `PATH`; prepend `C:\Program Files (x86)\Microsoft Visual Studio\Installer`
to `PATH` for the publish.

## Live smoke test (after step 5, 2026-10-19)

Game not running, Steam running. A temporary test drove `WorkshopActionRunner` with the real `ProcessRunner`
(`StartPiped`) and the Native AOT publish as `HelperExecutable`, action `Update` for up-to-date item 3778421142:
`started` → `itemDone` in ~2 s, outcome success, no failure. `HKCU\Software\Valve\Steam\RunningAppID` went 0 →
108600 when the helper started and back to 0 about 4 s later (Steam stops showing PZ after the helper exits). The
item's acf entry was unchanged; no helper process left behind. Not exercised live: subscribe / unsubscribe, Steam
closed, Linux.
