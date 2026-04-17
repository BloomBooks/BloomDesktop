#!/bin/bash
# Manual testing for CollectionTopBarControls with real Bloom backend
# Requires Bloom to be running on localhost:8089

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARENT_NAME="$(basename "$(dirname "$COMPONENT_DIR")")"
COMPONENT_NAME="$PARENT_NAME/$(basename "$COMPONENT_DIR")"

cd "$COMPONENT_DIR/../../component-tester"

./show-component-with-bloom.sh "$COMPONENT_NAME" "$@"
