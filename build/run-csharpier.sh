#!/bin/sh
# This script is run by Husky from src/BloomBrowserUI/package.json. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..
echo Formatting any C# files that are being submitted
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Feed this list to csharpier.
filesToFormat=filesToFormat.lst
git diff --cached --name-only --diff-filter=AM -z -- '*.cs' | tr '\0' '\n' >$filesToFormat
if [ -s $filesToFormat ]; then
  status=0
  while IFS= read -r file; do
    dotnet csharpier format --log-level Debug "$file" || status=$?
  done < $filesToFormat
else
  status=0
fi
rm $filesToFormat
exit $status
