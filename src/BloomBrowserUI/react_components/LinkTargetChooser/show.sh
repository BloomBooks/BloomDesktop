#!/bin/bash
# Manual testing for LinkTargetChooserDialog
# Uses Playwright with full mock support from test-helpers.ts
# Usage: ./show.sh [test-name]
# Example: ./show.sh hash-only-url-with-page-id

cd "$(dirname "$0")/../component-tester"

# Use the first argument as the test name, default to "default"
TEST_NAME="${1:-default}"

echo "Starting interactive manual testing mode..."
echo "Running test: $TEST_NAME"
echo "The browser will stay open until you close the Playwright Inspector or press Ctrl+C"
echo ""

# Set environment variable to suppress any auto-opening
export BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN=1
export PLAYWRIGHT_INCLUDE_MANUAL=1

yarn playwright test ../LinkTargetChooser/component-tests/manual.uitest.ts --headed -g "$TEST_NAME"
