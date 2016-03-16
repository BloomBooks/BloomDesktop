#!/bin/bash

# The helpful people at Debian have renamed the node program to nodejs.
# Here is their explanation and justification for breaking every compiled
# node.js script.
# ---
# The upstream name for the Node.js interpreter command is "node".
# In Debian the interpreter command has been changed to "nodejs".
#
# This was done to prevent a namespace collision: other commands use
# the same name in their upstreams, such as ax25-node from the "node"
# package.
#
# Scripts calling Node.js as a shell command must be changed to instead
# use the "nodejs" command.
# ---
# Hence this script, which searches for and fixes compiled node.js scripts in
# this directory and below.

find . -name '*.js' -type f -print0 | xargs -0 grep -l '^#!/usr/bin/env node$' >filesToFix.lst
for f in $(cat filesToFix.lst); do
    echo fixing "$f"
    mv -i "$f" "$f-0"
    sed 's=/usr/bin/env node=/usr/bin/env nodejs=' "$f-0" >"$f"
    chmod +x "$f"
    rm "$f-0"
done
