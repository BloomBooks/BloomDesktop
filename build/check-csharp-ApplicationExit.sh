#!/bin/sh
# This script is run by the pre-commit hook in src/BloomBrowserUI/.vite-hooks/pre-commit. We need to get to
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
      # EXPLORATION BRANCH ONLY - see the note in check-csharp-robustfile.sh.
      #
      # Unlike the RobustFile exemption, this one is arguably CORRECT rather than a debt. This rule says
      # "use Bloom's ProgramExit, not Application.Exit", and it exists because Bloom's shutdown has to be
      # sequenced. The Freeze Doctor is a different application with its own Program and its own message
      # loop; Application.Exit is simply how a WinForms app closes, and Bloom's ProgramExit means nothing
      # to it.
      #
      # Which is the finding worth taking from this branch: these gates all assume "every .cs file here is
      # Bloom". A second application in the repo needs them to gain a notion of WHICH application a file
      # belongs to, and that is a change to shared tooling rather than to the Doctor.
      src/BloomFreezeDoctor/*|src/BloomFreezeDoctor.Core/*|src/BloomFreezeDoctor.Tests/*) continue;;
    esac
    # Strip // line comments and /* ... */ single-line block comments before
    # searching, so a mention of Application.Exit in a comment doesn't trip this check.
    if sed -e 's#//.*##' -e 's#/\*.*\*/##g' "$file" | grep -H --label="$file" 'Application.Exit'; then
      status=0
      break
    fi
  done < $filesToCheck
fi
rm $filesToCheck
# if grep finds instances of unwanted calls, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
