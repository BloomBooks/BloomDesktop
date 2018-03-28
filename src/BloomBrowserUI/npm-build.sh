#!/bin/bash -i
set -e
cd "$(dirname "$0")"
nvm use 8.10.0
npm install
npm run build
npm test
