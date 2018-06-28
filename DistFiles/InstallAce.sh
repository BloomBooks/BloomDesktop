#!/bin/bash
# This script installs the Ace by Daisy ePUB checker in a place where
# Bloom can find it and users can easily use it from the command line.
# (This is obviously for use on debian based Linux systems such as
# ubuntu.)

# we need this for the next line if the user doesn't already have it.
sudo apt-get install -y curl

# The next two lines come from https://nodejs.org/en/download/package-manager/.
curl -sL https://deb.nodesource.com/setup_8.x | sudo -E bash -
sudo apt-get install -y nodejs

# The next line comes from https://daisy.github.io/ace/help/troubleshooting/
sudo /usr/bin/npm install -g @daisy/ace --unsafe-perm=true --allow-root

# The installation from "npm install -g" has broken permissions: only root can
# access a lot of files added for chrome.  Fix that by giving everyone read
# permission for everything.
cd /usr/lib/node_modules/@daisy/ace/node_modules/puppeteer/.local-chromium/
sudo chmod --recursive +r *
