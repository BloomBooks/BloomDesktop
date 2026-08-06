#!/bin/sh
# This script is run by the pre-commit hook in src/BloomBrowserUI/.vite-hooks/pre-commit. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..

missing_dependencies=
for dependency in git awk tr; do
  if ! command -v "$dependency" >/dev/null 2>&1; then
    missing_dependencies="$missing_dependencies $dependency"
  fi
done

if [ -n "$missing_dependencies" ]; then
  echo "Missing required commands for build/check-csharp-robustfile.sh:$missing_dependencies"
  echo "This hook runs in a POSIX shell environment and requires Git plus common Unix tools."
  echo "If you are committing from Windows, make sure the hook is running under Git Bash or equivalent."
  exit 1
fi

echo Checking for possible uses of non-Robust C\# File or FileStream operations.
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Screen out the C# files that we know may have calls using SIL.IO.File safely
# 3) Screen out all the test code since we don't need to worry about robustness there
# 4) For any remaining files, look for possible uses of (SIL.IO.)File.<method>.  (This
#    is complicated slightly by a set of localization IDs which use File.) We also look
#    for FileStream constructors and certain (SIL.IO.)Directory methods.
#    A FileStream use may be explicitly allowed if a nearby line contains
#    `robustfile-hook: allow FileStream`.
# 5) If anything is found (grep returns 0), then we stop everything and complain.
filesToCheck=filesToCheck.lst
git diff --cached --name-only --diff-filter=AM -z -- '*.cs' | tr '\0' '\n' >$filesToCheck
status=1
if [ -s $filesToCheck ]; then
  while IFS= read -r file; do
    case "$file" in
      src/BloomExe/RobustFileIO.cs) continue;;
      src/BloomTests/*) continue;;
    esac
    if awk '
      # Flag ordinary banned file APIs directly.
      # For FileStream, allow a narrow opt-out only when a nearby line contains
      # `robustfile-hook: allow FileStream`; this is intended for rare documented
      # cases such as requiring FileShare.ReadWrite | FileShare.Delete.
      /robustfile-hook:[[:space:]]*allow FileStream/ {
        allow_filestream_until = NR + 12;
      }
      /PublishTab\.Android\.File\./ {
        next;
      }
      /(^|[^A-Za-z0-9_])File\.[A-Z]/ ||
      /(^|[^A-Za-z0-9_])Directory\.(Move|Delete)([^A-Za-z0-9_]|$)/ ||
      /(^|[^A-Za-z0-9_])(Document|Metadata)\.FromFile([^A-Za-z0-9_]|$)/ {
        print FILENAME ":" $0;
        found = 1;
        next;
      }
      /new[[:space:]]+FileStream/ {
        if (NR > allow_filestream_until) {
          print FILENAME ":" $0;
          found = 1;
        }
      }
      END {
        exit found ? 0 : 1;
      }
    ' "$file"; then
      status=0
      break
    fi
  done < $filesToCheck
fi
rm $filesToCheck
# if grep finds instances of unwanted methods, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
