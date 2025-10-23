#!/bin/bash
c
export NODE_PATH="$(dirname "$0")/../component-tester/node_modules:$NODE_PATH"
cd "$(dirname "$0")"/component-tests && yarn test
