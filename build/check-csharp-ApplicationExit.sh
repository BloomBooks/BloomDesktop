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
filesToCheck=filesToCheck.lst
git diff --cached --name-only --diff-filter=AM -z -- '*.cs' | tr '\0' '\n' >$filesToCheck
status=1
if [ -s $filesToCheck ]; then
  while IFS= read -r file; do
    case "$file" in
      src/BloomExe/ProgramExit.cs) continue;;
      src/BloomTests/*) continue;;
    esac
    if grep -H 'Application.Exit' "$file"; then
      status=0
      break
    fi
  done < $filesToCheck
fi
rm $filesToCheck
# if grep finds instances of unwanted calls, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
