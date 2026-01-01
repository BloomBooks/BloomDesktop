#!/bin/bash
# Manual testing for color-picking in a shared, remote-debugging-enabled browser session.
# This prints (and attempts to open) a URL that renders ColorPickerManualHarness
# via the Vite component tester, so external tools can attach via remote debugging.

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}" )" && pwd)"
cd "$COMPONENT_DIR/../component-tester"

./show-scope.sh \
    "../color-picking/component-tests/colorPickerManualHarness" \
    "ColorPickerManualHarness"
