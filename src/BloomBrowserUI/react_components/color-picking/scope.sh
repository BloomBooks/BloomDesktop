#!/bin/bash
# Manual testing for color-picking in a normal browser (MCP-friendly).
# This prints (and attempts to open) a URL that renders ColorPickerManualHarness
# via the Vite component tester, so chrome-devtools-mcp can connect.

set -euo pipefail

COMPONENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}" )" && pwd)"
cd "$COMPONENT_DIR/../component-tester"

./show-component-mcp.sh \
    "../color-picking/component-tests/colorPickerManualHarness" \
    "ColorPickerManualHarness"
