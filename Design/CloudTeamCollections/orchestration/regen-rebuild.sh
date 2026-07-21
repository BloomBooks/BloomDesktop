#!/bin/bash
# Rebuild cloud-tc-for-review per SQUASH-PLAN.md: 9 path-staged commits from cloud-collections.
set -e
GRPDIR="$1"
cd /c/github/BloomDesktop

declare -a MSGS
MSGS[1]="Cloud Team Collections: design docs, wire contracts, and project records"
MSGS[2]="Cloud TC server: tc schema, RLS policies, RPCs, and pgTAP tests"
MSGS[3]="Cloud TC server: edge functions for checkin/download/collection-file transactions"
MSGS[4]="Cloud TC dev stack: local Supabase + MinIO, seed users, S3 parity checks"
MSGS[5]="Cloud TC client core: auth, API client, repo cache, S3 transfer (AWSSDK v4)"
MSGS[6]="Cloud TC backend: cache-backed TeamCollection, polling monitor, join flow, background downloads, account-switch handling"
MSGS[7]="Cloud TC API: sharing/membership endpoints, capabilities, join cards, book-list merge"
MSGS[8]="Cloud TC UI: sign-in, sharing dialog, status panel, join cards, download placeholders"
MSGS[9]="Cloud TC E2E: Playwright-over-CDP harness and 10 two-instance scenarios"

for i in 1 2 3 4 5 6 7 8 9; do
  echo "=== Group $i: $(wc -l < "$GRPDIR/g$i.txt") files ==="
  # xargs handles the long path list; -d '\n' preserves paths with spaces
  xargs -d '\n' -a "$GRPDIR/g$i.txt" git checkout cloud-collections --
  xargs -d '\n' -a "$GRPDIR/g$i.txt" git add --
  git commit -q --no-verify -m "${MSGS[$i]}

Part of the Cloud Team Collections feature (S3 + Supabase backed Team
Collections). This branch is a regenerable review-grained packaging of the
cloud-collections working branch; only the final tree is test-verified (see
Design/CloudTeamCollections/orchestration/SQUASH-PLAN.md).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
  echo "=== Group $i committed ==="
done

echo "=== Verifying byte identity ==="
if [ -n "$(git diff cloud-tc-for-review cloud-collections --stat)" ]; then
  echo "IDENTITY CHECK FAILED:"
  git diff cloud-tc-for-review cloud-collections --stat
  exit 1
fi
echo "IDENTITY OK: tree matches cloud-collections exactly"
git log --oneline origin/master..HEAD
