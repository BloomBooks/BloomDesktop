#!/bin/bash
# Manual debugging for BookGridSetup with real Bloom backend
# Requires Bloom to be running on localhost:8089

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

export BLOOM_COMPONENT_TESTER_USE_BACKEND=1
yarn scope "$@"
