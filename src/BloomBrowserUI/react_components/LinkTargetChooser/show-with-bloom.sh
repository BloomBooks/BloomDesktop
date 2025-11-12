#!/bin/bash
# Manual testing for LinkTargetChooserDialog with real Bloom backend
# Requires Bloom to be running on localhost:8089

cd "$(dirname "$0")/../component-tester"

echo "Starting interactive manual testing mode with real Bloom backend..."
echo "Make sure Bloom is running on localhost:8089"
echo "The browser will stay open until you close the Playwright Inspector or press Ctrl+C"
echo ""

# Set environment variable to use real backend instead of mocks
export BLOOM_COMPONENT_TESTER_USE_BACKEND=1
export BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN=1
export PLAYWRIGHT_INCLUDE_MANUAL=1

yarn playwright test ../LinkTargetChooser/component-tests/manual.uitest.ts --headed -g "with-bloom-backend"
