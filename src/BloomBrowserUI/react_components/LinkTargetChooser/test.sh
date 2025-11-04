#!/bin/bash
cd "$(dirname "$0")/../component-tester"
yarn test LinkTargetChooser "$@"
