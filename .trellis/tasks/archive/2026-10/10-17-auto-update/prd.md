# Launcher auto-update

## Goal

Users get new launcher versions without visiting a release page: the launcher checks for a newer stable release, downloads the package for its platform in the background, and replaces itself after a restart. Release data comes from whichever of GitHub or Gitee is reachable first (so mainland China users, for whom GitHub is often slow or blocked, are served by the Gitee mirror), and the source can be pinned in Settings.

Original request: 实现自动更新，从github或gitee检测版本是否有更新。先检测用户是否处于中国大陆，如果是在中国大陆，走gitee，否则走github。更新源可以在设置界面配置。 (D2 replaces the geolocation step with a reachability race, by the user's choice.)

## Background (repository evidence)

- Releases: `.github/workflows/release.yml` builds on `v*` tags; version = tag without `v`, passed as `-p:Version`; a tag with `-` is a prerelease. Manual runs use `0.0.0-ci.<run>`; local builds have no `-p:Version`. Existing releases: `v0.1.0`, `v0.2.0` (Latest).
- Assets: `PzLauncher-<version>-win-x64.zip`, `PzLauncher-<version>-linux-x64.tar.gz` (Native AOT, extracted-folder app, no installer). No macOS asset.
- GitHub `pz-launcher/launcher`: `GET https://api.github.com/repos/pz-launcher/launcher/releases/latest` returns `tag_name`, `prerelease`, and per asset `name`, `size`, `digest` (`sha256:<hex>`), `browser_download_url`.
- Gitee mirror `dontcampy/pzl` (`.github/workflows/gitee-sync.yml`): `GET https://gitee.com/api/v5/repos/dontcampy/pzl/releases/latest` returns `tag_name`, `prerelease`, and assets with only `name` + `browser_download_url`, plus two auto-generated source archives (`v0.2.0.zip`, `v0.2.0.tar.gz`). Gitee assets can lag GitHub by up to an hour after a release.
- No network code exists yet. Settings page: `PzLauncher.Desktop/ViewModels/Settings/SettingsViewModel.cs`, sections in `SettingsSection.cs`. Preferences contract: `.trellis/spec/desktop/preferences.md`. Running-game state: `PzLauncher.Desktop/Services/GameRunState.cs`. Close-time flush: `PzLauncher.Desktop/Views/WindowPlacementPersistence.cs`, wired in `App.axaml.cs`.

## Decisions

- D1 Download and install: the package for the running platform downloads in the background; the user is prompted, and the launcher restarts and replaces itself.
- D2 Source "Auto" (default): no geolocation. Query GitHub and Gitee concurrently and use the first source that answers successfully; the download comes from that same source.
- D3 Settings source options: Auto, GitHub, Gitee. A pinned source is used alone (no fallback to the other).
- D4 Timing: one automatic check per launcher start; Settings has "Check now" and an "Automatically check for updates" toggle (default on). The toggle only affects the startup check.
- D5 Stable only: a release whose tag contains `-` is never offered.
- D6 Prompt: non-modal banner at the top of the main window, "Version X downloaded — Restart and update / Later". "Later" hides the banner; the replacement happens when the launcher closes, so the next start runs version X.

Technical defaults (in design.md, listed here because users can observe them):

- D7 Dev and CI builds (current version `0.0.0-*`) never check automatically or install; "Check now" reports that updates are unavailable for development builds.
- D8 If the launcher's folder is not writable, nothing is downloaded; the banner says version X is available and offers "Open download page" (the release page of the winning source) instead of "Restart and update".
- D9 "Restart and update" is disabled while a game started from this launcher session is running (restarting would lose the run tracking); "Later" still installs on close.
- D10 A downloaded package is verified before install: the SHA-256 digest when the source provides one (GitHub), and always that the archive extracts and contains the launcher executable. A failed check discards the package.
- D11 Startup-check failures are logged only; "Check now" shows the error in Settings. A package downloaded but not yet installed (e.g. the close was an OS shutdown) is re-offered at the next start without downloading again.

## Requirements

- R1 Determine the running version from the assembly's informational version (metadata after `+` ignored) and compare SemVer against the latest stable release of the chosen source (D2, D3, D5, D7).
- R2 Auto source races both APIs with a per-request timeout; a source "answers successfully" when it returns a parseable stable release. The losing request is cancelled. If both fail, the check fails with a network error.
- R3 Pick the asset named `PzLauncher-<version>-<rid>.<zip|tar.gz>` for the running RID (`win-x64`, `linux-x64`); Gitee source archives are ignored. A release without the platform's asset counts as "no update" for that check.
- R4 Download and stage the package next to the running launcher, verify it (D10), and offer it (D6). Progress is visible in Settings.
- R5 Install replaces only files present in the new package; other files in the folder are untouched. A failure mid-install restores the previous files and reports an error. Leftovers from the previous version are removed at the next start.
- R6 "Restart and update" installs, starts the new executable, and exits (D9). "Later" installs during the close-time flush (D6, D11).
- R7 Settings gains an "Updates" section: current version, source (Auto/GitHub/Gitee), auto-check toggle, "Check now", and status (checking / up to date / downloading n% / ready / error).
- R8 Preferences persist the source and the toggle with stable strings (`updateSource`: `auto`/`github`/`gitee`; `autoCheckUpdates`: bool); missing values load as Auto / on.
- R9 All new user-facing text is in every `UiText*.resx`; new error codes follow `.trellis/spec/core/error-handling.md`.

## Acceptance Criteria

- AC1 (R1, D5) With current version 0.2.0 and a latest stable 0.3.0, an update is found; with latest 0.2.0, or only a newer prerelease, "up to date".
- AC2 (R2, D2) In Auto, when Gitee answers first the release and download URL come from Gitee; when Gitee fails, GitHub's answer is used; when both fail, the check returns a network error. Covered by tests with fake HTTP handlers.
- AC3 (D3) With source GitHub, Gitee is never contacted, and vice versa.
- AC4 (R3) Gitee's `v0.3.0.zip` source archive is never chosen; a release lacking the platform asset yields "no update".
- AC5 (D10) A GitHub package whose SHA-256 doesn't match the digest, or an archive without the executable, is discarded with an error and never installed.
- AC6 (R5) Install over a fake launcher folder replaces packaged files, leaves unrelated files, and on an injected failure leaves the folder exactly as before.
- AC7 (D4, D7) With the toggle off no request is made at start; "Check now" still checks. A `0.0.0-dev` build makes no request at start.
- AC8 (D6, D9) The banner appears after a successful download; "Restart and update" is disabled while `GameRunState.IsGameRunning`; "Later" hides it.
- AC9 (R8) Preference round-trip, missing, unknown-value, keeps-other-fields and unwritable-file tests pass for both new fields.
- AC10 (R9) Resx parity tests pass; `dotnet build` (warnings as errors) and `dotnet test PzLauncher.slnx` pass.
- AC11 Manual smoke (Windows): from an extracted older package in a writable folder, start → banner → "Restart and update" relaunches as the new version; repeat with "Later" + close → next start is the new version; set source Gitee and confirm the download host is gitee.com.

## Out of Scope

- Geolocation or IP lookup; prerelease/beta channel; skipping a specific version; macOS packages; release notes rendering in-app.
- Changes to `release.yml` / `gitee-sync.yml` (e.g. publishing checksums to Gitee).
- Self-update for the future `PzLauncher.Server`.
- Delta updates, signing/Authenticode verification, elevation for protected folders.
