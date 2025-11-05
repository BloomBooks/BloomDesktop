#!/bin/bash
# Run automated UI tests for this component
if [ "$1" = "--ui" ]; then
    cd "$(dirname "$0")"/.. && cd component-tester && yarn test:ui bookLinkSetup/component-tests
else
    cd "$(dirname "$0")"/.. && cd component-tester && yarn test bookLinkSetup/component-tests
fi
