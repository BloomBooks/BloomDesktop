# branding-report — moved

This tool now lives in its own repository:

**https://github.com/BloomBooks/branding-viewer**

It was moved so that a tester with only a CI-built Bloom, and no development environment, can run
it. Two things used to tie it to this repo:

1. It built its branding list by reading `src/content/branding` out of a checkout.
2. It drove a set-state handler that existed only inside `#if DEBUG`.

Both are gone. The tool now asks the running Bloom what it can survey, over endpoints that are
gated at runtime by the `--e2e` flag rather than by build configuration, so an ordinary Release
build answers them.

## What stayed here

The host half of the contract, in `src/BloomExe/web/controllers/E2eTestingApi.cs`:

```
GET  /bloom/api/e2e/surveyOptions   the brandings/layouts/xmatter packs THIS build ships,
                                    plus its version and current state
POST /bloom/api/e2e/setState        {branding, layout, xmatter}; omitted fields unchanged
```

Change either shape in both repos together. A tester may be running an older Bloom than the one
you are developing against, so treat `surveyOptions.bloomVersion` as the capability signal.

## How to run it

See `.claude/skills/branding-viewer/SKILL.md` in this repo for the quick path, including the
`--e2e` launch that `./go.sh` does not do for you. The hard-won capture-loop details that used to
be in `README-gotchas.md` here moved with the code.
