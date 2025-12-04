#!/bin/sh
# This script is run by Husky from src/BloomBrowserUI/package.json. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..
echo Checking for calling Application.Exit rather than Program.Exit.
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Screen out the one files that is allowed to use Application.Exit
# 3) Screen out all the test code since we don't need to worry about zombie processes there
# 4) For any remaining files, look for possible uses of Application.Exit.
# 5) If anything is found (grep returns 0), then we stop everything and complain.
git status --porcelain=v1 --untracked=no --ignored=no --no-renames | grep '^[AM]' | cut -c4- | grep '\.cs$' | \
  grep -v 'src/BloomExe/ProgramExit.cs$' | \
  grep -v 'src/BloomTests/' | \
  xargs grep -H 'Application.Exit'
status=$?
# if grep finds instances of unwanted calls, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
