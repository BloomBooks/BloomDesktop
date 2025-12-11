#!/bin/bash
# Manual testing for WorkspaceTopRightControls
# Uses Playwright with full mock support from component-tester
# Usage: ./show.sh [test-name]
# Example: ./show.sh default

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARENT_NAME="$(basename "$(dirname "$COMPONENT_DIR")")"
COMPONENT_NAME="$PARENT_NAME/$(basename "$COMPONENT_DIR")"

cd "$COMPONENT_DIR/../../component-tester"

./show-component.sh "$COMPONENT_NAME" "$@"
