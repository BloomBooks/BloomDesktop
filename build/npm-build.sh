#!/bin/bash -i
set -e
cd "$(dirname "$0")/../src/BloomBrowserUI"

NPMSKIPINSTALL=false
NPMSKIPTEST=false
while (( "$#" )); do
    case "$1" in
	"-i"|"--skip-install")
	    NPMSKIPINSTALL=true
	    ;;
	"-t"|"--skip-test")
	    NPMSKIPTEST=true;
	    ;;
	*) echo "Usage: npm-build.sh [options]";
	   echo "  -i|--skip-install - skip 'npm install' before building"
	   echo "  -t|--skip-test    - skip 'npm test' after building"
	   exit 1;
	   ;;
    esac
    shift
done

# nvm-windows modifies the environment in such a way that neither bash scripts
# nor batch files can find npm after "nvm use" is called.  This requires nvm
# to run in a subshell for the sake of Windows.
if [ "$OS" = "Windows_NT" ]; then
    (nvm ls | grep '\* 8\.10\.0') || (nvm use 8.10.0)
else
    (nvm current | grep '8\.10\.0') || nvm use 8.10.0
fi

if [ "$NPMSKIPINSTALL" != "true" ]; then npm install; fi
npm run build
if [ "$NPMSKIPTEST" != "true" ]; then npm test; fi
