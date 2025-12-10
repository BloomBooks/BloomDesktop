#!/bin/bash
# Manual testing for CollectionTopBarControls (requires Bloom backend)
# Uses the shared component tester harness
# Usage: ./show.sh [test-name]
# Example: ./show.sh with-bloom-backend

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPONENT_NAME="$(basename "$COMPONENT_DIR")"

cd "$COMPONENT_DIR/../component-tester"

# This component depends on Bloom APIs/websockets, so we default to the backend-aware runner.
./show-component-with-bloom.sh "$COMPONENT_NAME" "${1:-with-bloom-backend}" "${@:2}"
