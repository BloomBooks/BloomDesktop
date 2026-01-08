#!/bin/bash
# Manual testing for RegistrationContents
# Uses Playwright with full mock support from test-helpers.ts
# Usage: ./show.sh [test-name]
# Example: ./show.sh with-existing-info

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPONENT_NAME="$(basename "$COMPONENT_DIR")"

cd "$COMPONENT_DIR/../component-tester"

./show-component.sh "$COMPONENT_NAME" "$@"
