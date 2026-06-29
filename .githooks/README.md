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
