# Modpacks: share a Workshop collection with a mod preset

User request (verbatim): 设计一种可以将订阅合集和模组预设一起分享的格式。并且支持一键订阅合集。

## Goal

A player (e.g. a server owner) shares one text that says "subscribe to this collection, then play with this mod
preset". The receiver pastes it into the launcher and, with one button, gets every missing Workshop item subscribed
and the preset imported. To support this the launcher gains two managed sections: 本地合集 (named lists of Workshop
items kept by the launcher) and 整合包 (modpacks: a collection plus a preset, saved, shared and imported).

## Background (repository facts)

- Mod presets are the game's lists in `Lua/pz_modlist_settings.cfg` of each install's cache folder (code names them
  `ModCollection*`). They hold mod ids only. Share text is the game's line `name:id1;id2;...;`
  (`PzLauncher.Core/Mod/Collections/ModCollectionShareText.cs:8`); the launcher's preset import
  (`PzLauncher.Desktop/ViewModels/Mods/Collections/ModCollectionsViewModel.cs:135`) asks before overwriting; delete confirms
  (same file :212). The current preset is per install (`defaultModCollections` in `preferences.json`).
- Workshop favorites share text uses fixed ASCII markers `#collections` / `#items`
  (`PzLauncher.Core/Steam/Workshop/WorkshopFavoritesShareText.cs:10`); favorites and opened-collection history are
  launcher-wide preference lists (`WorkshopEntryList`).
- Subscribing runs in the Steam helper (`WorkshopAction.Subscribe`,
  `PzLauncher.Core/Steam/Workshop/Actions/WorkshopActionRequest.cs:3`) and needs Steam running. An opened collection
  already offers 订阅缺失项 / 生成本地预设 (`PzLauncher.Desktop/ViewModels/Mods/Workshop/WorkshopItemListSectionViewModel.cs:56`, `:63`).
  `IWorkshopService.GetCollectionAsync` expands nested collections depth-first, each item once.
- Installed Workshop mods know their item id, so a preset's mods map to Workshop items where they are installed.
- The game can rename or delete presets without the launcher seeing it first.

## Decisions (user, 2026-10-21)

- D1 Carrier: clipboard text only. One import dialog; one button subscribes and imports. The preset line keeps the
  game's format so it can be pasted into the game alone.
- D2 Contents: one collection + one preset + the Workshop items of preset mods that the collection lacks.
  Non-Workshop mods can't be shared; the receiver sees them as missing.
- D3 The collection is a Steam Workshop collection (shared by id; the receiver reads its current contents) or a local
  collection (shared as its item ids, a snapshot).
- D4 A local collection is filled by pasting links/ids, by "添加到本地合集…" on item rows, and by "另存为本地合集" on
  an opened Steam collection; items can be removed and reordered, and 生成本地预设 follows that order.
- D5 本地合集 is a new 模组 sidebar section managing many local collections; "添加到本地合集…" opens a dialog to pick
  or create the target.
- D6 After import: a received local collection is saved, a received Steam collection joins 最近打开, and the launcher
  then shows the imported modpack with each item's state. Without Steam the data is still imported; the failed
  subscribe is reported and 订阅缺失项 retries it.
- D7 A dedicated 模组 sidebar section 整合包 (modpack) does modpack share and import; 模组预设's 分享 / 导入 stay
  game-format only.
- D8 Modpacks are saved and managed (new, rename, delete with confirmation, share).
- D9 A modpack links its preset and local collection and keeps a copy of each. Deleting a used preset or local
  collection in the launcher warns, naming the modpacks: delete and make them independent, delete them too, or
  cancel. A linked source that disappears outside the launcher also makes the modpack independent; an independent
  modpack can be relinked. Steam collections are always referenced by id.
- D10 Modpacks are per install, like presets; local collections are launcher-wide.

## Requirements

Local collections (D3–D5)
- R1 Section 本地合集 between 工坊合集 and 本地收藏: list of local collections (new, rename, delete); the selected one's
  items on the right with the opened-collection columns and state, 订阅缺失项, 更新过期项, 生成本地预设 (name prefilled
  with the collection's name), open in Steam per row.
- R2 Fill and edit: paste Workshop links/ids (one per line; ids the Web API reports as collections are skipped and
  counted); remove an item; reorder by drag and by move commands, as in 模组预设.
- R3 "添加到本地合集…" on item rows of 工坊订阅 (also for the selection there), an opened Steam collection and
  本地收藏的物品: a dialog picks an existing local collection or creates one by name; items already in it are skipped,
  new ones go to the end.
- R4 "另存为本地合集" on an opened Steam collection: name prompt prefilled with its title; items in collection order.
- R5 Local collections persist launcher-wide with ids, last known titles and order; names are unique, non-empty and
  single-line.

Modpacks (D7–D10)
- R6 Section 整合包 right after 模组预设, per install: list of modpacks (new, rename, delete with confirmation); the
  selected one shows its preset (linked name, or 独立 with the copy's name) and collection (Steam title + id, local
  name, or 独立), then its items: collection items in order, then extra items (D2), with state, 订阅缺失项, 更新过期项.
- R7 New / edit: pick a preset of the install and a collection (Steam: from 本地收藏, 收藏的合集, 我的合集, 最近打开,
  or a pasted link/id; local: from 本地合集). Relinking an independent modpack is the same edit.
- R8 使用预设 makes the modpack's preset the install's current preset, as 模组预设 does. For an independent preset it
  first writes the copy into the game's presets (a name clash asks: overwrite or new name) and relinks to it.
- R9 Links and copies (D9): the copy follows the linked source whenever the source is read; a rename in the launcher
  updates links; a delete in the launcher of a used preset (模组预设) or local collection (本地合集) asks with the three
  D9 choices; a preset missing after a successful read of the install's presets, or a local collection no longer
  stored, makes the modpack independent. A failed read never changes links.
- R10 Persistence: modpacks per install root in `preferences.json`, names unique per install.

Share and import (D1, D2, D6)
- R11 分享 copies the modpack share text (format in `design.md`): modpack name, the collection (Steam id, or local
  name + item ids), the extra items, and the preset as the game's line, last. The status names how many preset mods
  could not be shared (not from the Workshop / not installed).
- R12 导入 in 整合包: multiline prompt prefilled from the clipboard when it holds modpack text; then a confirmation
  listing modpack name, collection, extra-item count and preset (name + mod count), with one button 订阅并导入.
  Name clashes (modpack in this install, preset in this install, local collection) each ask: overwrite or new name;
  cancelling any of them imports nothing.
- R13 On accept: write the preset, save the local collection or remember the Steam collection, save the modpack
  linked to them, then subscribe every unsubscribed item of the collection and the extra items in one Steam action,
  and select the imported modpack (D6). The preset write needs the game not running, as preset import does today.
- R14 Text from a newer format version is refused with a message; 模组预设's 导入 given modpack text points to 整合包.

## Acceptance Criteria

- AC1 (R1, R5) Create, rename and delete local collections; they survive a restart and are the same for every install.
- AC2 (R2) Pasting links and ids adds the items in paste order, skips duplicates and collection ids with a count;
  drag and move commands reorder; 生成本地预设 follows that order.
- AC3 (R3) "添加到本地合集…" from each of the three places adds to a picked or newly created collection; existing
  items are not duplicated.
- AC4 (R4) "另存为本地合集" saves the opened collection's items in collection order.
- AC5 (R6, R7, R10) A modpack made from a preset and a Steam or local collection lists collection items then extra
  items with state; modpacks of one install are not shown for another.
- AC6 (R8) 使用预设 marks the preset current; on an independent modpack it restores the preset first.
- AC7 (R9) Deleting a linked preset or local collection offers the three choices and each does what it says;
  deleting or renaming the preset in the game file makes the modpack independent on the next read; a failed read
  does not; renaming in the launcher keeps the link.
- AC8 (R11) Share text round-trips through the parser; its last line, pasted into the game's import, adds the preset.
- AC9 (R12, R13) Importing on another machine/install creates the preset, the collection (local saved / Steam in
  最近打开) and the modpack, subscribes exactly the unsubscribed collection + extra items in one Steam action, and
  shows the modpack. With Steam not running the data is imported and the subscribe error is shown.
- AC10 (R12) Each name clash asks; cancel writes nothing.
- AC11 (R14) A newer version is refused; modpack text in 模组预设's 导入 points to 整合包.
- AC12 Every new string exists in every resx; `preferences.md` documents the two new fields.

## Out of Scope

- `pzlauncher://` links / protocol handler, and sharing as a file (D1).
- Sharing a local collection on its own (it travels inside a modpack).
- Modpacks shared across installs (D10); launching from Home by modpack.
- Creating or editing Steam Workshop collections; syncing a received modpack with later changes by the sharer.
- Sharing non-Workshop mods.
