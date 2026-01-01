#!/usr/bin/env bash
set -euo pipefail

./build/getDependencies-windows.sh &
(cd src/content && yarn install) &
(cd src/BloomBrowserUI && yarn install) &
dotnet build src/WebView2PdfMaker &

wait

(cd src/BloomBrowserUI && yarn build)
