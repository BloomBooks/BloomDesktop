#!/bin/sh
# This script is run by Husky from src/BloomBrowserUI/package.json. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..
echo Formatting any C# files that are being submitted
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Feed this list to csharpier.
git status --porcelain=v1 --untracked=no --ignored=no --no-renames | grep '^[AM]' | cut -c4- |\
  grep '\.cs$' | sed 's=^\(.*\)$="\1"=' >filesToFormat.lst
if [ -s filesToFormat.lst ]; then
  cat filesToFormat.lst | xargs dotnet csharpier format --log-level Debug
  status=$?
else
  status=0
fi
rm filesToFormat.lst
exit $status
