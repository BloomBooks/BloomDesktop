#!/bin/bash
# Run automated UI tests for this component
cd "$(dirname "$0")"/.. && cd component-tester && yarn test registration/component-tests
