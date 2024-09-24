#!/bin/sh
# This script is run by Husky from src/BloomBrowserUI/package.json. We need to get to
# the root of the git repository, which is one level up from where this script lives.
cd $(dirname $0)/..
echo Checking for possible uses of C\# XmlNode based classes.
# 1) Get the list of C# files that have been staged for commit (added or modified)
# 2) Screen out the C# files that we know may be using XmlNode based classes safely
# 3) Search for any lines matching the XmlNode based class names.  (Unfortunately,
#    this includes commments so those must be adjusted properly as well.)
# 4) If anything is found (grep returns 0), then we stop everything and complain.
git status --porcelain=v1 --untracked=no --ignored=no --no-renames | grep '^[AM]' | cut -c4- | grep '\.cs$' | \
  grep -v '/SafeXml/SafeXml' | \
  grep -v '/CollectionChoosing/MostRecentPathsList.cs$' | \
  grep -v '/BloomTests/FluentAssertXml.cs$' | \
  xargs grep -H '[^A-Za-z0-9_]Xml\(Document\|Element\|Node\|Attribute\|Text\|Whitespace\|CDataSection\|Comment\|CharacterData\)[^A-Za-z0-9_]'
status=$?
# if grep finds instances of unwanted class names, then stop the commit.
[ $status -eq 0 ] && exit 1 || exit 0
