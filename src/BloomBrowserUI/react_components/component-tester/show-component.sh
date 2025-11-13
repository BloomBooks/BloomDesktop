#!/bin/bash
# Shared harness for manual Playwright-driven component testing
# Usage: ./show-component.sh <ComponentFolderName> [test-name]
# Example: ./show-component.sh LinkTargetChooser page-only-url

set -euo pipefail

if [ $# -lt 1 ]; then
    echo "Usage: $0 <ComponentFolderName> [test-name]" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPONENTS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

COMPONENT_FOLDER="$1"
shift

MANUAL_TEST_RELATIVE_PATH="$COMPONENT_FOLDER/component-tests/manual.uitest.ts"
MANUAL_TEST_FULL_PATH="$COMPONENTS_DIR/$MANUAL_TEST_RELATIVE_PATH"

if [ ! -f "$MANUAL_TEST_FULL_PATH" ]; then
    echo "Manual Playwright suite not found for component '$COMPONENT_FOLDER'" >&2
    echo "Expected at: $MANUAL_TEST_RELATIVE_PATH" >&2
    exit 1
fi

TEST_NAME="default"
if [ $# -gt 0 ]; then
    TEST_NAME="$1"
    shift
fi

cd "$SCRIPT_DIR"

echo "Starting interactive manual testing mode..."
echo "Component: $COMPONENT_FOLDER"
echo "Running test: $TEST_NAME"
echo "The browser will stay open until you close the Playwright Inspector or press Ctrl+C"
if [ "${BLOOM_COMPONENT_TESTER_USE_BACKEND:-0}" = "1" ]; then
    echo "Bloom backend mode: ensure Bloom is running on localhost:8089"
fi
echo ""

export BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN=1
export PLAYWRIGHT_INCLUDE_MANUAL=1

yarn playwright test "../$MANUAL_TEST_RELATIVE_PATH" --headed -g "$TEST_NAME" "$@"
