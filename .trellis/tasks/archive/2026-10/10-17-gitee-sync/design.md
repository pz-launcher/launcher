# Design: Gitee sync

## Shape

One new workflow, `.github/workflows/gitee-sync.yml`, with two jobs:

| Job | Trigger | Does |
|-----|---------|------|
| `mirror-main` | `push` to `main` | full-history checkout, `git push --force` `main` to Gitee |
| `release` | `workflow_call` (input `tag`) and `workflow_dispatch` (input `tag`) | push the tag, create/update the Gitee Release, upload missing assets |

`release.yml` gets a final job `gitee` (`needs: release`, tag runs only) that
calls `gitee-sync.yml` with `tag: ${{ github.ref_name }}` and
`secrets: inherit`.

Why a call instead of `on: release`: the GitHub Release is created with
`GITHUB_TOKEN`, and events raised by `GITHUB_TOKEN` never trigger other
workflows, so `release: published` would not fire.

## Release job data flow

1. Checkout with full history and tags; `git push --force <gitee> refs/tags/<tag>`.
2. `gh release view <tag> --json name,body,isPrerelease` → name/notes/flag.
   Empty body falls back to the tag name (Gitee requires `body`).
3. `gh release download <tag> -D dist` → the assets.
4. `GET /releases/tags/<tag>`: exists → `PATCH` name/body/prerelease;
   missing → `POST /releases` with `target_commitish` = tag commit SHA.
5. `GET /releases/{id}/attach_files` → names already attached; `POST`
   `attach_files` (multipart `file`) for each asset not yet present.

The Gitee work runs in its own job after `release`, so its failure leaves the
GitHub Release untouched (R5).

## Credentials

- Git: `https://dontcampy:${GITEE_TOKEN}@gitee.com/dontcampy/pzl.git`, built
  from env at use time; never echoed.
- API: `access_token` sent as form/query field via `curl`, `--fail-with-body`
  so HTTP errors fail the step.
- Owner/repo (`dontcampy/pzl`) as workflow-level env so a rename touches one
  place.

## Rollback

Delete `gitee-sync.yml` and the `gitee` job in `release.yml`; nothing else
depends on them.
