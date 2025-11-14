#!/bin/bash
# Shared harness for manual testing against a running Bloom backend
# Usage: ./show-component-with-bloom.sh <ComponentFolderName> [test-name]

set -euo pipefail

if [ $# -lt 1 ]; then
    echo "Usage: $0 <ComponentFolderName> [test-name]" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

COMPONENT_FOLDER="$1"
shift

TEST_NAME="with-bloom-backend"
if [ $# -gt 0 ]; then
    TEST_NAME="$1"
    shift
fi

cd "$SCRIPT_DIR"

export BLOOM_COMPONENT_TESTER_USE_BACKEND=1
export BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN=1
export PLAYWRIGHT_INCLUDE_MANUAL=1

"$SCRIPT_DIR/show-component.sh" "$COMPONENT_FOLDER" "$TEST_NAME" "$@"
