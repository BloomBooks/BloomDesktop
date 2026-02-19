#!/bin/bash
# Run automated UI tests for this component
set -e

script_dir="$(cd "$(dirname "$0")" && pwd)"
cd "$script_dir/../component-tester"

yarn test "../registration/component-tests" "$@"
