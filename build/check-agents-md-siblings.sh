#!/bin/sh
# Run by the pre-commit hook in src/BloomBrowserUI/.vite-hooks/pre-commit.
#
# Claude Code auto-loads a nested CLAUDE.md but never a nested AGENTS.md, so every AGENTS.md
# below the repo root needs a one-line CLAUDE.md beside it containing "@AGENTS.md" (the root
# already has one). This check fails the commit when a staged AGENTS.md has no such sibling
# in the INDEX (a sibling that exists on disk but is not staged would not be committed), so
# nobody has to remember the rule. It reports every offender before failing.
cd "$(dirname "$0")/.."
echo "Checking that every nested AGENTS.md has a CLAUDE.md sibling importing it."
missing=$(git diff --cached --name-only --diff-filter=AMR -z -- 'AGENTS.md' '*/AGENTS.md' | tr '\0' '\n' | while IFS= read -r file; do
  [ -n "$file" ] || continue
  sibling="$(dirname "$file")/CLAUDE.md"
  if ! git cat-file -e ":$sibling" 2>/dev/null || ! git show ":$sibling" | grep -q '^@AGENTS.md'; then
    echo "$file"
  fi
done)
if [ -n "$missing" ]; then
  echo "$missing" | while IFS= read -r file; do
    echo "  $file has no staged $(dirname "$file")/CLAUDE.md importing it. Create it with the single line: @AGENTS.md, and stage it."
  done
  exit 1
fi
exit 0
