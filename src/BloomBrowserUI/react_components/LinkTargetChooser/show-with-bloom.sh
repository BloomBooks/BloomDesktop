#!/bin/bash
# Manual testing for LinkTargetChooserDialog with real Bloom backend
# Requires Bloom to be running on localhost:8089

set -euo pipefail

cd "$(dirname "$0")/../component-tester"

./show-component-with-bloom.sh "LinkTargetChooser" "$@"
