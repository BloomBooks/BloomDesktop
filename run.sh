#!/usr/bin/env bash
set -euo pipefail

# Build-once launcher (contrast with ./go.sh, which runs Bloom under `dotnet watch`).
# Builds BloomExe once (Debug) and launches the built Bloom.exe directly — no C#
# file watcher, so the output tree is not held locked and rebuilds are unobstructed.
# The front-end still hot-reloads via Vite. See src/BloomBrowserUI/scripts/run.mjs.
exec node ./src/BloomBrowserUI/scripts/run.mjs "$@"
