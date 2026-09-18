#!/bin/sh
# Run by the pre-commit hook in src/BloomBrowserUI/.vite-hooks/pre-commit.
#
# Claude Code auto-loads a nested CLAUDE.md but never a nested AGENTS.md, so every AGENTS.md
# below the repo root needs a one-line CLAUDE.md beside it containing "@AGENTS.md" (the root
# already has one). This check fails the commit when a staged AGENTS.md has no such sibling, so
# nobody has to remember the rule.
cd "$(dirname "$0")/.."
echo "Checking that every nested AGENTS.md has a CLAUDE.md sibling importing it."
status=0
git diff --cached --name-only --diff-filter=AMR -z -- 'AGENTS.md' '*/AGENTS.md' | tr '\0' '\n' | while IFS= read -r file; do
  [ -n "$file" ] || continue
  dir=$(dirname "$file")
  sibling="$dir/CLAUDE.md"
  if [ ! -f "$sibling" ] || ! grep -q '^@AGENTS.md' "$sibling"; then
    echo "  $file has no $sibling importing it. Create it with the single line: @AGENTS.md"
    exit 1
  fi
done || status=1
exit $status
