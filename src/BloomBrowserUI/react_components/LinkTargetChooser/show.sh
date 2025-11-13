#!/bin/bash
# Manual testing for LinkTargetChooserDialog
# Uses Playwright with full mock support from test-helpers.ts
# Usage: ./show.sh [test-name]
# Example: ./show.sh page-only-url

set -euo pipefail

cd "$(dirname "$0")/../component-tester"

./show-component.sh "LinkTargetChooser" "$@"
