#!/bin/sh
# This script is run by Husky from src/BloomBrowserUI/package.json. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..
echo Checking for possible uses of non-Robust C\# File or FileStream operations.
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Screen out the C# files that we know may have calls using SIL.IO.File safely
# 3) Screen out all the test code since we don't need to worry about robustness there
# 4) For any remaining files, look for possible uses of (SIL.IO.)File.<method>.  (This
#    is complicated slightly by a set of localization IDs which use File.) We also look
#    for FileStream constructors and certain (SIL.IO.)Directory methods.
# 5) If anything is found (grep returns 0), then we stop everything and complain.
git status --porcelain=v1 --untracked=no --ignored=no --no-renames | grep '^[AM]' | cut -c4- | grep '\.cs$' | \
  grep -v 'src/BloomExe/RobustFileIO.cs$' | \
  grep -v 'src/BloomTests/' | \
  xargs grep -H '\([^A-Za-z0-9_]File\.[A-Z]\|new\s\+FileStream\|[^A-Za-z0-9_]Directory\.\(Move\|Delete\)[^A-Za-z0-9_]\|[^A-Za-z0-9_]\(Document\|Metadata\)\.FromFile\)' | \
  grep -v 'PublishTab\.Android\.File\.'
status=$?
# if grep finds instances of unwanted methods, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
