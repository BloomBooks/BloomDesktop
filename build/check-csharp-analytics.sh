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
  echo "Missing required commands for build/check-csharp-analytics.sh:$missing_dependencies"
  echo "This hook runs in a POSIX shell environment and requires Git plus common Unix tools."
  echo "If you are committing from Windows, make sure the hook is running under Git Bash or equivalent."
  exit 1
fi

echo Checking that analytics events go through BloomAnalytics.
# Why: DesktopAnalytics decides whether to send an event INSIDE Analytics.Track, and in a DEBUG
# build it decides not to (Program.InitializeAnalytics passes allowTracking:false). An event that
# calls it directly is therefore impossible to observe on a developer machine -- it neither sends
# nor says it didn't. BloomAnalytics.Track logs every event before handing it on, which is the only
# way to see one without shipping to alpha. That log is only worth reading if it is complete, so a
# call that bypasses it stops the commit: a missing line has to mean "did not happen" rather than
# "somebody used the other API".
#
# 1) Get the list of C# files that have been staged for commit (added or modified).
# 2) Skip the wrapper itself, which necessarily calls DesktopAnalytics.
# 3) Look for Analytics.Track / Analytics.ReportException that are NOT BloomAnalytics.<method>.
#    The [^A-Za-z0-9_] guard is what distinguishes them: it rejects "BloomAnalytics.Track(" (the
#    preceding character is a letter) while catching both "Analytics.Track(" and the fully
#    qualified "DesktopAnalytics.Analytics.Track(".
# 4) If anything is found, stop everything and complain.
filesToCheck=filesToCheck.lst
git diff --cached --name-only --diff-filter=AM -z -- '*.cs' | tr '\0' '\n' >$filesToCheck
status=1
if [ -s $filesToCheck ]; then
  while IFS= read -r file; do
    case "$file" in
      src/BloomExe/BloomAnalytics.cs) continue;;
    esac
    if awk '
      # A commented-out call is not a call, and this repo has explanatory comments that name the
      # very API this script bans -- flagging those would block a commit for no reason.
      /^[[:space:]]*(\/\/|\*|\/\*)/ { next }
      /(^|[^A-Za-z0-9_])Analytics\.(Track|ReportException)[[:space:]]*\(/ {
        print FILENAME ":" FNR ": " $0;
        found = 1;
      }
      END {
        exit found ? 0 : 1;
      }
    ' "$file"; then
      echo "Use BloomAnalytics.Track / BloomAnalytics.ReportException instead, so the event is"
      echo "logged and can be seen in a build where tracking is off. See src/BloomExe/BloomAnalytics.cs."
      status=0
      break
    fi
  done < $filesToCheck
fi
rm $filesToCheck
# if we found instances of the unwrapped API, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
