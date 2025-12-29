#!/bin/bash
# Run automated UI tests for this component
set -e

script_dir="$(cd "$(dirname "$0")" && pwd)"
cd "$script_dir/../component-tester"

component_path="../color-picking/component-tests"

if [ "${1:-}" = "--ui" ]; then
    shift
    yarn test:ui "$component_path" "$@"
else
    yarn test "$component_path" "$@"
fi
