#!/bin/bash
# Manual testing for LinkTargetChooserDialog
# Uses Playwright with full mock support from test-helpers.ts

cd "$(dirname "$0")/../component-tester"

echo "Starting interactive manual testing mode..."
echo "The browser will stay open until you close the Playwright Inspector or press Ctrl+C"
echo ""

# Set environment variable to suppress any auto-opening
export BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN=1

yarn playwright test ../LinkTargetChooser/component-tests/manual.uitest.ts --headed -g "default"
