#!/bin/bash
# Manual testing for BookGridSetup with real Bloom backend
# Requires Bloom to be running on localhost:8089

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPONENT_NAME="$(basename "$COMPONENT_DIR")"

cd "$COMPONENT_DIR/../component-tester"

./show-component-with-bloom.sh "$COMPONENT_NAME" "$@"
