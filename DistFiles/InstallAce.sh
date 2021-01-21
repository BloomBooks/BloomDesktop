#!/bin/bash
# This script installs the Ace by DAISY ePUB checker in a place where
# Bloom can find it and users can easily use it from the command line.
# (This is obviously for use on debian based Linux systems such as
# ubuntu.)

if [ $UID -eq 0 ]; then
    echo Run this script without using sudo.  Any needed sudo commands are inside the script.
    exit 1
fi

# we need this for the next line if the user doesn't already have it.
sudo apt-get install -y curl

# The next two lines come from https://nodejs.org/en/download/package-manager/.
curl -sL https://deb.nodesource.com/setup_12.x | sudo -E bash -
sudo apt-get install -y nodejs

# The next line comes from https://daisy.github.io/ace/help/troubleshooting/.
sudo /usr/bin/npm install -g @daisy/ace --unsafe-perm=true --allow-root

# The installation from "npm install -g" has broken permissions: only root can
# access a lot of files added for chrome.  Fix that by giving everyone read
# permission for everything.
sudo chmod --recursive +r /usr/lib/node_modules/@daisy/ace/node_modules/puppeteer/.local-chromium/*

# The installation also leaves some files in the user's area belonging to root.
# This also needs to be fixed.
sudo chown -R $USER:$(id -gn $USER) $HOME/.config
