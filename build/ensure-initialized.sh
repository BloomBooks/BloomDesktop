#!/bin/bash
set -e

echo "=========================================================="
echo "Bloom Desktop - Ensure Development Environment Initialized"
echo "=========================================================="
echo ""

# Get the repository root directory (parent of the build directory)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"


echo "Checking for Bloom.chm file in DistFiles..."
echo "--------------------------------------"
if [ ! -e "$REPO_ROOT/DistFiles/Bloom.chm" ]; then
    echo "Bloom.chm file not found. Running getDependencies-windows.sh..."
    cd "$SCRIPT_DIR"
    bash getDependencies-windows.sh
else
    echo "Bloom.chm file found. Skipping getDependencies-windows.sh."
fi
echo ""


echo "Checking node_modules in content directory..."
echo "--------------------------------------"
cd "$REPO_ROOT/src/content"
if [ ! -d "node_modules" ]; then
    echo "node_modules not found. Running yarn in content directory..."
    yarn
else
    echo "node_modules already exists in content directory. Skipping yarn."
fi
echo ""


echo "Checking node_modules in BloomBrowserUI directory..."
echo "--------------------------------------"
cd "$REPO_ROOT/src/BloomBrowserUI"
if [ ! -d "node_modules" ]; then
    echo "node_modules not found. Running yarn and yarn build in BloomBrowserUI directory..."
    yarn
    yarn build
else
    echo "node_modules already exists in BloomBrowserUI directory. Skipping yarn and yarn build."
fi
echo ""

echo "=========================================================="
echo "✓ Development environment initialization complete!"
echo "=========================================================="
echo ""
echo "You can now build and run Bloom Desktop."
