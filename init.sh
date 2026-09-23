#!/usr/bin/env bash
set -euo pipefail

# Run the independent setup steps in parallel, but remember which process is which so we can
# wait on each one and report exactly what failed. A bare "wait" (with no arguments) always
# returns 0, so without this every one of these steps could fail -- a build server that is
# down, no network, a broken pnpm registry -- and init.sh would cheerfully carry on to the
# front-end build and then exit 0.
declare -A jobs=()

./build/getDependencies-windows.sh &
jobs[$!]="./build/getDependencies-windows.sh (C# binary dependencies from build.palaso.org)"
(cd src/content && pnpm install) &
jobs[$!]="pnpm install in src/content"
(cd src/BloomBrowserUI && pnpm install) &
jobs[$!]="pnpm install in src/BloomBrowserUI"
dotnet build src/WebView2PdfMaker &
jobs[$!]="dotnet build src/WebView2PdfMaker"

failures=()
for pid in "${!jobs[@]}"
do
	if ! wait "$pid"
	then
		failures+=("${jobs[$pid]}")
	fi
done

if [ ${#failures[@]} -ne 0 ]
then
	echo "" >&2
	echo "**********************************************************************" >&2
	echo "*** init.sh FAILED. These steps did not succeed:" >&2
	for failure in "${failures[@]}"
	do
		echo "***   $failure" >&2
	done
	echo "***" >&2
	echo "*** Look above for their output. This checkout is NOT ready to build;" >&2
	echo "*** the front-end build was skipped. Fix the problem and run ./init.sh again." >&2
	echo "**********************************************************************" >&2
	exit 1
fi

(cd src/BloomBrowserUI && pnpm run build)
