#!/bin/bash
# Manual debugging for LinkTargetChooserDialog
# Uses scope-harness.tsx + the shared component-tester scope runner.
# Usage: ./show.sh [exportName]
# Example: ./show.sh pageOnlyUrl

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

yarn scope "$@"
