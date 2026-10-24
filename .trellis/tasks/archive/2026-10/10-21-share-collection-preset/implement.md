# Implementation plan

Three stages, each building on the previous and ending in a green build; each can be its own commit.

## Stage 1 — Local collections (R1–R5)

1. Preferences: `localCollections` field (load with invalid-entry dropping, save method), `preferences.md` row.
2. `LocalCollections` service + tests (create, rename, delete, add/skip duplicates, remove, move, titles, persistence).
3. `ModsSection.LocalCollections`, sidebar entry, `LocalCollectionsViewModel` + view (list, items, paste add, remove,
   reorder, 生成本地预设).
4. `PickLocalCollectionAsync` dialog; "添加到本地合集…" in 工坊订阅 (row + selection), opened collection, 本地收藏.
5. "另存为本地合集" on an opened Steam collection.
6. Strings in every resx; DesignData samples.

## Stage 2 — Modpacks (R6–R10)

1. Preferences: `modpacks` field + `preferences.md` row.
2. `Modpacks` service + tests (per install, link/copy refresh, independence on missing source, rename, delete users).
3. Extra-items computation + tests (rules in `design.md`).
4. `ModsSection.Modpacks`, `ModpacksViewModel` + view: list, new/edit/rename/delete, item list, 使用预设.
5. Delete warnings in `ModCollectionsViewModel.DeleteAsync` and the local-collection delete; rename hook.
6. Strings, DesignData.

## Stage 3 — Share and import (R11–R14)

1. Core `ModpackShareText` Format/Parse + tests (round trip, markers ignoring case, links, skipped lines, unknown
   markers, newer version, missing sections, numeric names).
2. 分享 in 整合包.
3. 导入: prompt, confirmation, clash resolution, writes, one subscribe action, select modpack; VM tests with fakes
   (Steam not running, cancel on clash, game running).
4. 模组预设 导入 pointing to 整合包 for modpack text.
5. Strings.

## Validation

- Build: `dotnet build` (implement); tests: `dotnet test` (check stage only).
- Manual: share from one install, import into another (or after deleting the preset/collection) with Steam running and
  not running; paste the last line into the game's preset import.

## Risky points

- `ModCollectionsViewModel` delete/rename flow (existing behaviour must stay when no modpack uses the preset).
- Reorder/drag code reuse between 模组预设 and 本地合集.
- Link maintenance on read must ignore failed reads (AC7).
