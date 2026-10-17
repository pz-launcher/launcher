# Implement: Gitee sync

## Checklist

1. Add `.github/workflows/gitee-sync.yml` (jobs `mirror-main`, `release`) per
   design.md; `permissions: contents: read`; `GH_TOKEN: ${{ github.token }}`
   for `gh release view/download`.
2. Add job `gitee` to `.github/workflows/release.yml`: `needs: release`,
   `if: github.ref_type == 'tag'`, `uses: ./.github/workflows/gitee-sync.yml`,
   `with: tag`, `secrets: inherit`.
3. Comments: only non-obvious why (e.g. why `workflow_call` instead of
   `on: release`, why forced push).

## Validation

- `actionlint` on both workflow files if available; otherwise YAML parse
  (`python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))"`).
- Static review against AC1–AC5 (CI runs need the `GITEE_TOKEN` secret, which
  the user adds; live verification happens after merge:
  push to `main` → AC1, manual dispatch for `v0.2.0` twice → AC3).

## Risky points

- Shallow checkout cannot push to an empty remote → `fetch-depth: 0`.
- Token leakage via `set -x` or echoed remote URLs → never enable tracing.
