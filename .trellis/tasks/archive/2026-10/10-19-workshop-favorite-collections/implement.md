# Implement — favorited and own Steam collections

Implement runs `dotnet build` only; tests run in check.

## 1. Core

- [x] Bind the query exports in `SteamApi` (CreateQueryUserUGCRequest, SetReturnChildren, SendQueryUGCRequest,
      GetQueryUGCResult, ReleaseQueryUGCRequest, SteamUser GetSteamID); payload readers for call result 3401 and
      `SteamUGCDetails_t` at both packings.
- [x] Helper list action + result event; paging; read-only.
- [x] `IWorkshopActionRunner.ListCollectionsAsync` + DTOs in `CoreJsonContext`; current-account lookup exposed for
      Desktop.
- [x] Core tests (written, run in check): protocol round trip, payload decoding (both packings, UTF-8 title), runner mapping and paging via
      fake helper output.

## 2. Desktop

- [x] Preference field + store tests; spec row in `desktop/preferences.md`.
- [x] `WorkshopCollectionsViewModel` groups, auto-fetch once per session, refresh, status, gating; view; resx keys in
      all 10 files; DesignData.
- [x] Desktop tests per AC5 (written, run in check).

## 3. Check and live verification

- [x] trellis-check: build, all tests, format, AOT publish 0 warnings.
- [x] Live (read-only, game closed, Steam running): AC1 ids/titles/counts; AC3 with Steam closed is left to the user.
- [ ] Commit and push to PR #15.

Rollback point: after step 1 (Core only, no UI).
