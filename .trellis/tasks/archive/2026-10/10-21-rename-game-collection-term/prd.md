# Rename in-game mod collection term

## Goal

The launcher uses "合集" (and "Collection" in other languages) for three things: the game's own mod lists, Steam Workshop collections, and starred Workshop collections in 本地收藏. Rename the game's mod lists (`ModCollection` in code, named lists in `Lua/pz_modlist_settings.cfg`) to the term the game's own mod selector uses, so players can tell them apart and recognise them from the game.

## Background

Game terms, from `ProjectZomboid/media/lua/shared/Translate/<LANG>/UI.json` (keys `UI_mods_SavePrompt`, `UI_mods_preset_choose`, `UI_modselector_help3`): EN "mods preset", CN 模组预设, CH 模組預設, ES "conjunto de mods", ES_MX "preajuste de mods", ES_CL empty, PT "modelo de mods", PTBR "predefinição de mods", RU "набор модов". The launcher's name validation already mirrors the game's `SandboxOptions.isValidPresetName` ([ModCollectionName.cs:5](../../../PzLauncher.Core/Mod/Collections/ModCollectionName.cs)).

Decision: bare 预设 / "preset" reads as unclear to players, so the term is always written in full with "mod(s)".

## Requirements

- R1 Term per launcher language (always the full form; grammatical forms such as plural or Russian case are fine, dropping the "mod(s)" part is not):

  | resx | Term |
  |---|---|
  | `UiText.resx` (en) | mod preset |
  | `zh-Hans` | 模组预设 |
  | `zh-Hant` | 模組預設 |
  | `ru` | набор модов |
  | `es-ES` | conjunto de mods |
  | `es-MX`, `es-AR`, `es-CL` | preajuste de mods (game has no es-AR and an empty ES_CL, so they follow ES_MX) |
  | `pt-BR` | predefinição de mods |
  | `pt-PT` | modelo de mods |

- R2 Rename every text that refers to the game mod list, in all 10 resx files. Keys (English text containing "collection" for the game concept):
  - `Home_Collection`, `Home_NoCollection`
  - `Error_ModCollectionNameInvalid`, `Error_ModCollectionModIdInvalid`, `Error_ModCollectionNotFound`
  - `Steam_HookHint`
  - `Mods_SectionCollections`, `Mods_AddToCollection`, `Mods_ColumnCollections`
  - every `ModCollections_*` key whose text names the concept
  - `Workshop_FilterUnused`, `Workshop_UnsubscribeUsedMessage`
  - `WorkshopCollections_Generate`, `_GenerateHint`, `_GenerateTitle`, `_Generated`, `_PickMessage` — only the part that means the launcher/game list (zh-Hans 本地合集, zh-Hant 本機合集, en "mod collection"/"collection" for the new list); the Workshop collection part stays.
  - `ModApply_KeepMessage`
- R3 Texts that mean Steam Workshop collections keep their current wording: `Mods_SectionWorkshopCollections`, other `WorkshopCollections_*`, `Error_WorkshopCollection*`, `WorkshopFavorites_*`. After the change, zh-Hans 合集 (zh-Hant 合輯) means only Workshop collections.
- R4 Placeholders (`{0}`, `{1}`) and the key set stay identical across languages (localization spec parity rule).

## Acceptance Criteria

- [ ] AC1 (R1, R2) In each resx, every R2 key uses that language's R1 term; none of them still uses the old word for the game concept (zh-Hans 合集/本地合集, zh-Hant 合集/本機合集, en collection, ru сборка, es colección, pt coleção).
- [ ] AC2 (R1) zh-Hans has no 预设 not preceded by 模组; zh-Hant has no 預設 not preceded by 模組; English has no "preset" not preceded by "mod".
- [ ] AC3 (R3) `git diff` touches no R3 key.
- [ ] AC4 (R4) `UiTextResourcesTests` passes; the solution builds.
- [ ] AC5 Only `PzLauncher.Desktop/Lang/*.resx` change.

## Out of Scope

- Code identifiers (`ModCollection*`, `ErrorCode.ModCollection*`, resx keys).
- User data: preset names, share text format, `preferences.json`.
- Workshop collection wording (R3).
- Native-speaker review of non-Chinese wording beyond using the game's term.
