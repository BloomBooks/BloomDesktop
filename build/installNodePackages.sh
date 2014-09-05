#!/bin/bash
# Copyright (c) 2014 SIL International
# This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
#
# Install node dependencies for BloomBrowserUI, found in package.json
#

# install karma modules globally
npm install -g karma
npm install -g karma-chrome-launcher
npm install -g karma-cli
npm install -g karma-firefox-launcher
npm install -g karma-jasmine

# install the modules found in package.json
npm install
