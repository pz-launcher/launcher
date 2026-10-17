# Sync main and releases to Gitee

## Goal

Mirror this repository's `main` branch and its releases to Gitee
(<https://gitee.com/dontcampy/pzl>) via GitHub Actions, so users who cannot
reach GitHub reliably can get the source and the release packages from Gitee.

## Background

- GitHub origin: `https://github.com/pz-launcher/launcher.git`; existing tags
  `v0.1.0`, `v0.2.0`.
- `.github/workflows/release.yml` builds Native AOT packages on `v*` tag push
  (`PzLauncher-<ver>-win-x64.zip`, `PzLauncher-<ver>-linux-x64.tar.gz`); its
  `release` job creates the GitHub Release (`--generate-notes`, `--prerelease`
  for SemVer suffixes). `workflow_dispatch` runs create no release.
- The Gitee repo `dontcampy/pzl` exists, is public, and is empty (no refs).
- Gitee API v5 offers create/get-by-tag/patch release and
  upload/list release attachments (`/releases/{id}/attach_files`).

## Requirements

- R1: every push to GitHub `main` updates Gitee `main` to the same commit.
  Gitee is a read-only mirror: the push is forced, so commits made directly on
  Gitee `main` are overwritten.
- R2: when the GitHub Release for a `v*` tag is created, Gitee gets the tag and
  a Release with the same name, the same notes, the same prerelease flag, and
  every GitHub Release asset attached.
- R3: the release sync can be run manually for a given tag, which also
  backfills `v0.1.0` and `v0.2.0`. Re-running it for a tag that already has a
  Gitee Release updates that release and uploads only missing attachments
  (no duplicates).
- R4: the Gitee token lives only in the GitHub secret `GITEE_TOKEN` and never
  appears in logs.
- R5: a Gitee failure does not fail or roll back the GitHub Release.

## Acceptance Criteria

- AC1 (R1): after a push to `main`, `git ls-remote` on Gitee shows `main` at
  the GitHub `main` SHA.
- AC2 (R2): after pushing a new `v*` tag, Gitee shows the tag and a Release
  whose notes and prerelease flag match GitHub and that lists both packages.
- AC3 (R3): manually running the sync for `v0.2.0` twice leaves exactly one
  Gitee Release with exactly the two packages attached.
- AC4 (R4): workflow logs contain no token value (masked secret, no token in
  echoed URLs).
- AC5 (R5): with an invalid `GITEE_TOKEN`, the GitHub Release still exists
  and only the Gitee job fails.

## Out of Scope

- Branches other than `main`, issues, PRs, wiki.
- Gitee → GitHub direction.
- Deleting Gitee releases/tags when they are deleted on GitHub.
- Creating the Gitee token / adding the secret (user does this; documented in
  the final report).
