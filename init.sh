#!/usr/bin/env bash
set -euo pipefail

./build/getDependencies-windows.sh &
(cd src/content && pnpm install) &
(cd src/BloomBrowserUI && pnpm install) &
dotnet build src/WebView2PdfMaker &

wait

(cd src/BloomBrowserUI && pnpm run build)
