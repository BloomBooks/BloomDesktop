# `.githooks/` — transitional pre-commit dispatcher

This directory exists **only** to bridge the front-end's migration from
**yarn + husky 4** to **pnpm + vite-plus (vp)**. It is temporary scaffolding and
is meant to be deleted once the migration is complete (see
[When this can be removed](#when-this-can-be-removed)).

## The problem it solves

Different branches use different git-hook systems during the transition:

| Branch type | Where its pre-commit checks live |
| --- | --- |
| pnpm / vite-plus | `src/BloomBrowserUI/.vite-hooks/pre-commit` |
| yarn / husky 4 | the default `.git/hooks/pre-commit` (installed by husky) |

Git decides which hook to run from `core.hooksPath`, and that setting:

- is chosen **before** git knows which branch is checked out, and
- is **shared across all worktrees** of a clone.

So if a worktree is set up for one system and you switch it to a branch that uses
the other, git runs the wrong hook — or, in the worst case, points at a hooks
directory that doesn't exist on that branch and **silently runs nothing**. Silent,
unchecked commits are exactly what we want to avoid.

## How it works

`core.hooksPath` is set to `.githooks`, and **this dispatcher is committed to every
maintained branch at the same path**. So whatever branch a worktree is on, git
always finds `.githooks/pre-commit`, and the dispatcher routes — at commit time, the
one moment branch-aware logic can run — to whichever checker that branch actually
ships:

1. if `src/BloomBrowserUI/.vite-hooks/pre-commit` exists → run the vite-plus checks;
2. else if a husky hook exists in `.git/hooks/pre-commit` → run that;
3. else → **fail loudly** with instructions, instead of skipping silently.

## What the vite-plus hook runs

Routing aside, `src/BloomBrowserUI/.vite-hooks/pre-commit` runs three groups of checks,
and the thing worth knowing before editing it is that they have deliberately different
reach:

- **Formatting** — `pretty-quick`, on **every commit, whatever it touches**. Despite living
  in the front-end folder, it is not scoped to it: `pretty-quick --staged` resolves the git
  root and formats every staged file prettier understands anywhere in the tree, filtered by
  `.prettierignore`. That is why the repo root has its own `.prettierignore`. The hook still
  runs it from `src/BloomBrowserUI`, because that is what applies *both* ignore files —
  including the front-end one that keeps prettier off vendored third-party code.
- **Lint and typecheck** — `lint-staged` (eslint) and a whole-project typecheck, **only when
  the commit stages a front-end source file** (`.ts`/`.tsx`/`.mts`/`.cts` and, since the
  tsconfig sets `allowJs`, `.js`/`.jsx`/`.mjs`/`.cjs` under `src/BloomBrowserUI/`). These two
  genuinely are confined to that directory, so a commit of only workflows, docs or C# gives
  them nothing to do.
- **C#** — the `build/check-csharp-*.sh` scripts and `build/run-csharpier.sh`. Every commit;
  they need only dotnet, never the front-end dependencies.

**Committing without `pnpm install`.** A worktree used only for workflow, docs or C# work
can commit without ever installing the front-end dependencies. Formatting still happens
there: if `node_modules` is absent the hook falls back to `pnpm dlx` at the exact prettier
and pretty-quick versions pinned in `package.json` (read from the file, so the fallback
cannot drift from what the project uses). An installed worktree always prefers its own
lockfile-verified binaries, so the normal case needs no network and works offline.

Two skips are worth telling apart. If front-end sources *are* staged and `node_modules` is
missing, the hook **fails loudly** — eslint and tsgo need the whole dependency graph, not one
tool, and silently skipping is how an unchecked front-end commit would get through. If only
the *formatting* fallback cannot run (no pnpm, no network), the hook **warns and lets the
commit through**: unformatted code is cosmetic, and blocking would put us back to a worktree
that cannot commit a docs change at all. Neither is the silent skip this dispatcher exists to
prevent — that one is "a hook system was configured but nothing ran".

## How to enable it (per clone)

`core.hooksPath` is git config, not a tracked file, so each clone sets it once:

```sh
git config core.hooksPath .githooks
```

On pnpm/vite-plus branches the install step (`prepare`) does this for you. On husky
branches you can run it manually (or wire it into the install) so the dispatcher is
active there too and you get the loud-fail safety net everywhere.

## When this can be removed

This is **transition-only**. Once every actively maintained branch has moved to
pnpm + vite-plus — i.e. there are no husky branches left that anyone checks out:

1. point `core.hooksPath` back at the vite-plus hooks directory (or let `vp config`
   manage it), and
2. delete this `.githooks/` directory and the dispatcher with it.

At that point every branch uses the same hook system, so a router is no longer
needed.
