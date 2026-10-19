# Research: listing the signed-in user's favorited PZ workshop collections

- **Query**: How can the launcher list the Steam Workshop collections the signed-in user has favorited for Project
  Zomboid (108600)? Route 1: Steamworks `CreateQueryUserUGCRequest` from a standalone process. Route 2: the public
  profile page `myworkshopfiles?browsefilter=myfavorites&section=collections`. Compare both and recommend one.
- **Scope**: mixed. Live read-only spike against this machine's Steam client, plus anonymous HTTP probes.
- **Date**: 2026-10-19

Labels: **[Spike]** observed in the throwaway console app. **[Probe]** anonymous HTTP request. **[Ref]** Steamworks SDK
headers (via `rlabrecque/SteamworksSDK` `steam_api.json` and the generated Steamworks.NET sources). **[Unverified]**
inferred, not tested.

Read-only: only query calls were made (`CreateQueryUserUGCRequest`, `SetReturnChildren`, `SendQueryUGCRequest`,
`GetQueryUGCResult`, `ReleaseQueryUGCRequest`, `GetSteamID`). There were no subscribe, favorite, download, or other
write calls, and nothing under the Steam folders was modified. The game was not running (the only `java.exe`
processes were Gradle and Kotlin daemons). Steam was already running and was not started by the spike.

Throwaway code (outside the repo):
`C:\Users\dontc\AppData\Local\Temp\claude\E--Dev-Project-pz-PzLauncher\e669cc74-ef3c-4dc0-94d2-dffe73bbb39c\scratchpad\fav-spike\Program.cs`
plus the `fav-spike-run.ps1` runner, which polls `RunningAppID`.

---

## Findings

### Files found (existing code this would build on)

| File | Relevance |
|---|---|
| `PzLauncher.Core/Steam/Workshop/Helper/SteamApi.cs` | Function-pointer binding, `Pump()` call-result plumbing (703 → `GetAPICallResult`). Needs about 6 more exports for this (below). |
| `PzLauncher.Core/Steam/Workshop/Helper/SteamApiTypes.cs` | `SteamCallbackIds`, `SteamCallbackPayloads` (pack 8 vs pack 4 offsets) |
| `PzLauncher.Core/Steam/Workshop/Helper/SteamHelperSession.cs` | `TryWaitForCallResult` already covers a query's call result |
| `PzLauncher.Core/Steam/SteamLoginUsers.cs` | Current account. `loginusers.vdf` on this machine has **no `MostRecent` key** (newer clients), so the existing timestamp fallback applies. Its account id equals `GetSteamID` & 0xFFFFFFFF [Spike] |

`HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` (Windows) also held the same account id while Steam was signed in
[Local].

### Route 1: Steamworks query (works)

**Enums and layouts verified** [Ref]:
- `EUserUGCList`: Published 0, VotedOn 1, VotedUp 2, VotedDown 3, WillVoteLater 4, **Favorited 5**, Subscribed 6,
  UsedOrPlayed 7, Followed 8.
- `EUGCMatchingUGCType`: Items 0, **Collections 3**, All = ~0 ("only valid for CreateQueryUserUGCRequest").
- `EUserUGCListSortOrder`: **CreationOrderDesc 0**, CreationOrderAsc 1, TitleAsc 2, LastUpdatedDesc 3, ...
- `EWorkshopFileType`: Community 0, Collection 2.
- Flat signatures:
  - `SteamAPI_ISteamUGC_CreateQueryUserUGCRequest(self, AccountID_t, EUserUGCList, EUGCMatchingUGCType, EUserUGCListSortOrder, AppId_t creator, AppId_t consumer, uint32 page) → UGCQueryHandle_t (uint64)`. Invalid handle = `0xFFFFFFFFFFFFFFFF`.
  - `SendQueryUGCRequest(self, handle) → SteamAPICall_t`
  - `GetQueryUGCResult(self, handle, uint32 index, SteamUGCDetails_t*) → bool`
  - `ReleaseQueryUGCRequest(self, handle) → bool`
  - `SetReturnChildren(self, handle, bool) → bool`
  - `SteamAPI_SteamUser_v023()` + `SteamAPI_ISteamUser_GetSteamID(self) → uint64`
- `SteamUGCQueryCompleted_t` = callback **3401** (`k_iSteamUGCCallbacks` 3400 + 1). Fields: `m_handle u64`,
  `m_eResult i32`, `m_unNumResultsReturned u32`, `m_unTotalMatchingResults u32`, `m_bCachedData bool`,
  `m_rgchNextCursor char[256]`. Size with pack 8 = **280**; the spike's call result reported size 280 [Spike].
- `SteamUGCDetails_t`, pack 8, **9784 bytes** [Spike: `sizeof` matched]. Offsets with pack 8:
  - 0 `m_nPublishedFileId u64`, 8 `m_eResult`, 12 `m_eFileType`, 16 `m_nCreatorAppID`, 20 `m_nConsumerAppID`
  - 24 `m_rgchTitle[129]`, 153 `m_rgchDescription[8000]`, 8160 `m_ulSteamIDOwner u64`
  - 8168 `rtimeCreated`, 8172 `rtimeUpdated`, 8176 `rtimeAddedToUserList`, 8180 `eVisibility`
  - 8184 three bools, 8187 `tags[1025]`, 9216 `hFile`, 9224 `hPreviewFile`, 9232 `fileName[260]`, 9492 `fileSize`,
    9496 `previewFileSize`, 9500 `url[256]`
  - 9756 votesUp, 9760 votesDown, 9764 score, **9768 `m_unNumChildren`**, 9776 `m_ulTotalFilesSize u64`
  - It is wrapped in the same `VALVE_CALLBACK_PACK` pragma as callbacks (Steamworks.NET declares it
    `Pack = Packsize.value`), so Linux would use pack 4 with different offsets [Ref; Linux not tested].
- **Exports**: every function above exists in PZ's `steam_api64.dll`. `NativeLibrary.TryGetExport` returned true for
  all of them [Spike].

**Results** (account = `GetSteamID` lower 32 bits; consumer = 108600; sort = CreationOrderDesc; page 1) [Spike]:

| List | Matching type | Creator app | Results | Notes |
|---|---|---|---|---|
| Favorited | Collections | 108600 | **0** | `eResult` 1 (OK), just empty |
| Favorited | Collections | 766 | 2 | 3623026433, 2690796635 |
| Favorited | Collections | **0** | **2** | same 2. 0 = no creator filter |
| Favorited | All (-1) | 0 | 6 | the 4 favorited items (creator 108600, fileType 0) + the 2 collections (creator 766, fileType 2) |
| Published | Collections | 108600 | 1 | 3815132834 "my" (the user's own collection), creator app **108600**, owner = self |
| Published | Collections | 766 | **0** | |
| Published | Collections | **0** | 1 | 3815132834 |

- The creator app filter is applied strictly. Favorited web-made collections have creator 766. The user's own
  collection has creator 108600 (it was presumably made in-game or through `CreateItem` [Unverified]). **Only
  creator = 0 returns both kinds.**
- The favorited collections are not the user's: `m_ulSteamIDOwner` ≠ self, `m_eVisibility` 0 (public).
  `m_rtimeAddedToUserList` is filled (the time each was favorited).
- `m_unNumChildren` is **0 unless `SetReturnChildren(handle, true)`** is set before sending. With it set: 201, 71, and
  214. These match keyless `GetCollectionDetails` child counts exactly [Spike + Probe]. The child ids themselves would
  come from `GetQueryUGCChildren`, which was not called.
- Paging: 50 results per page (`k_unNumUGCResultsPerPage`). `m_unTotalMatchingResults` gives the total. Not exercised
  past page 1, because there are only 2 favorites.
- Latency: about 300 ms per query (500 ms on the first creator-0 query). `m_bCachedData` = 0.
- Title strings are UTF-8 `char[129]`. The ASCII titles read correctly. Chinese titles showed as mojibake only because
  the runner's console pipe re-encoded them [Unverified that the raw bytes are fine, but it is expected].
- `RunningAppID` (HKCU\Software\Valve\Steam):
  - Run 1, 7 queries: 0 → 108600 about 0.25 s after start. The process exited 2.5 s later and the value returned to 0
    **about 2.3 s after exit**.
  - Run 2, 2 queries: 108600 for about 0.65 s of process life + 2.3 s after exit.
  - Both runs: init + all queries + `SteamAPI_Shutdown` ≈ 0.7 s to 2.6 s. stderr carried the usual two breakpad lines
    (already known from the archived spike). No errors.
- Private profile: whether the Steamworks query still works when the profile is private was not tested. Testing it
  would mean changing the user's privacy setting [Unverified, expected to work because the client is authenticated as
  that user].

### Route 2: public profile page (works, anonymous)

URL: `https://steamcommunity.com/profiles/<steamid64>/myworkshopfiles/?appid=108600&browsefilter=myfavorites&section=collections`,
where steamid64 = 76561197960265728 + accountId. No cookies and no login; only a plain `User-Agent` was sent [Probe].

- **Reachable without login**: HTTP 200, about 70 KB, about 1 s. `/profiles/<id64>/…` **302-redirects to
  `/id/<vanity>/myworkshopfiles?...`** when the user has a vanity URL, so follow redirects.
- **`section` matters**: `section=collections` lists collections. `section=items`, `section=` (empty), and no
  `section` all list **items only** (the 4 favorited mods). Without `appid` the list covers all games: 31 favorited
  collections across apps here, each tagged with `data-appid`.
- **Extraction**: each collection is an anchor
  `<a class="workshopItemCollection ugc_show_warning_image ugc" href="https://steamcommunity.com/sharedfiles/filedetails/?id=<id>" data-appid="108600" data-publishedfileid="<id>">`
  containing `div.workshopItemTitle` (title, HTML-escaped UTF-8), `span.workshopItemAuthorName`,
  `img.workshopItemPreviewImage` (preview URL), and `div.workshopItemShortDesc`. Items in `section=items` use a
  different wrapper (`div.workshopItem` > `a.ugc[data-appid][data-publishedfileid]`), so match on
  `data-publishedfileid` rather than on class names.
- **Paging**:
  - Default 9 per page. `numperpage` accepts only **9, 18, or 30**: 1, 50, and 100 fell back to 9. 30 gave
    "Showing 1-30 of 31".
  - `&p=N` selects the page. `div.workshopBrowsePagingInfo` reads "Showing a-b of N entries" (English text, so it may
    be localized). `div.workshopBrowsePagingControls` holds `a.pagelink` / `a.pagebtn` links with `&p=`.
  - A page past the end returns 200 with `#no_items` "No matching files were found for <persona>" and no paging info.
- **Other states** (from other, unrelated profiles; their identities are not recorded):
  - Private profile: HTTP 200 after a **redirect to the profile root** (`/profiles/<id>` or `/id/<vanity>`, with
    `myworkshopfiles` dropped). The page contains `div.profile_private_info` "This profile is private." Friends-only
    profiles look the same to an anonymous client [Unverified].
  - Unknown id: HTTP 200 with `div.error_ctn` "The specified profile could not be found."
  - Public profile with no favorites: 200 with `#no_items`.
- Freshness after favoriting or unfavoriting, and rate limits for repeated scrapes, were not measured (measuring
  freshness would need a write).

### Comparison

| | Route 1: Steamworks query | Route 2: profile page |
|---|---|---|
| Result on this account | Favorited collections {3623026433, 2690796635}; own {3815132834} | Favorited collections {3623026433, 2690796635} (same order); own (`browsefilter=myfiles&section=collections`) {3815132834} |
| Same set? | **Yes, identical ids**, and items (4) also match | |
| Needs | Steam running + signed in, PZ owned, PZ's `steam_api` DLL, helper process | Internet only, **public profile**, the account id (local files) |
| Side effects | Steam shows "playing PZ" for about 1 to 3 s of helper life + about 2.3 s after exit. Blocked while the game runs? (not tested; the archived spike ran actions alongside the game) | None |
| Private profile | Expected to work [Unverified] | Fails (redirect + `profile_private_info`) |
| Data | Structured: id, fileType, creator/consumer app, owner, visibility, time added, child count (with `SetReturnChildren`), title, preview handle | Id, title, author name, preview URL, short description. No child count (keyless `GetCollectionDetails` supplies it) |
| Paging | 50 per page, total count | 9/18/30 per page, HTML paging |
| Robustness | Stable SDK ABI; per-OS packing; Linux untested | Undocumented HTML that Valve may change; English-only strings for status text; vanity redirect |
| Gotchas | creator app **must be 0** (108600 misses web-made collections, 766 misses in-game-made ones); `SetReturnChildren` is needed for child counts | `section=collections` is required; private and not-found states are 200 responses |

### Recommendation (requested by the caller)

- Use **Route 1** as the primary source, run in the existing short-lived helper process as a new read-only action:
  `CreateQueryUserUGCRequest(account, Favorited 5, Collections 3, CreationOrderDesc 0, creator 0, consumer 108600, page)`
  + `SetReturnChildren(true)`, paging by 50 until `TotalMatching`.
  - Optionally run the same query with Published 0 to add "my collections".
  - It works with private profiles [Unverified] and returns structured data, and the helper plumbing (call results,
    pack 8 offsets) already exists.
  - Cost: a brief "playing PZ" blip of about 3 to 5 s per refresh and the Steam-running requirement. Cache the result
    and refresh on demand rather than on every page visit.
- **Route 2 as a fallback** when the helper can't run (Steam closed, no PZ DLL, Linux untested) and the profile is
  public. Parse `data-publishedfileid` with `numperpage=30`, follow redirects, detect `profile_private_info` /
  `error_ctn`, then enrich through keyless `GetPublishedFileDetails` / `GetCollectionDetails`. Trade-off: HTML
  scraping is brittle and silently empty for private profiles.
- Route 2 alone is enough if the brief "playing PZ" state is unacceptable, provided the UI explains the public-profile
  requirement.

## Caveats / Not found

- Paging beyond page 1 was not exercised on Route 1 (only 2 favorites). Route 2 paging was exercised on the all-apps
  list (31 / 62 entries).
- Route 1 was not run with the game running, with Steam closed, or on Linux (pack 4 `SteamUGCDetails_t` offsets
  differ).
- Route 1 behaviour for a private profile and Route 2 freshness after a favorite change were not tested, because both
  would need a write or a settings change.
- `GetQueryUGCChildren` was not called. The child ids are available from it, or from the keyless
  `GetCollectionDetails` that the launcher already uses.
- No official Valve documentation for the `myworkshopfiles` HTML. Everything there comes from observed markup on
  2026-10-19.

## Live verification of the implementation (2026-10-19)

Game not running, Steam running. A temporary test drove `WorkshopActionRunner.ListCollectionsAsync` with the real
`ProcessRunner` and the Native AOT publish as helper: success in ~0.8 s; Favorited 3623026433 (201 items, CJK title
decoded correctly) and 2690796635 (71); Published 3815132834 (214) — matches AC1. `RunningAppID` was 108600 for about
2 s, then 0. Not exercised live: Steam closed (AC3), paging past page 1, Linux.
