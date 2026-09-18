#!/bin/sh
# Run by the pre-commit hook in src/BloomBrowserUI/.vite-hooks/pre-commit.
#
# Claude Code auto-loads a nested CLAUDE.md but never a nested AGENTS.md, so every AGENTS.md
# below the repo root needs a one-line CLAUDE.md beside it containing "@AGENTS.md" (the root
# already has one). This check validates the whole INDEX (what the commit will contain), not
# just the staged AGENTS.md files, so it also catches a deleted or unstaged sibling. The repo
# has a handful of these files, so the full scan costs nothing. It reports every offender.
cd "$(dirname "$0")/.."
echo "Checking that every nested AGENTS.md has a CLAUDE.md sibling importing it."
missing=$(git ls-files --cached -z -- '*/AGENTS.md' | tr '\0' '\n' | while IFS= read -r file; do
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
