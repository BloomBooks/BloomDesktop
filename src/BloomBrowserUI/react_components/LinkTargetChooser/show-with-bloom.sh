#!/bin/bash
# Manual testing for LinkTargetChooserDialog with real Bloom backend
# Requires Bloom to be running on localhost:8089

cd "$(dirname "$0")/../component-tester"

yarn manual LinkTargetChooserDialog --backend
