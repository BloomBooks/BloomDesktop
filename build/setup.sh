#!/bin/bash
# Set up bloom repo

usage()
{
	echo "Usage: $(basename "$0") [URL]"
}

# Use bolding in messages, if possible
BOLD=$(tput bold 2>/dev/null)
NORM=$(tput sgr0 2>/dev/null)
SRCURL=https://raw.githubusercontent.com/sillsdev/gerrit-support/develop/

milepost()
{
	echo "$BOLD$@$NORM"
}

download()
{
	echo "	Downloading $1"
	if [ -f .git/$1 ]; then
		chmod +w .git/$1
	fi

	if hash curl 2>/dev/null; then
		curl -s -o .git/$1 $SRCURL/$1
	elif hash wget 2>/dev/null; then
		wget --output-document=.git/$1 $SRCURL/$1
	else
		echo "Can't find curl nor wget. Exiting."
		exit 1
	fi
	chmod +x .git/$1
	chmod -w .git/$1 # make it less likely that user changes it
}

# Check the current directory
if ! GIT_DIR=$(git rev-parse --git-dir)
then
	echo "You need to run this in a git working directory." >&2
	echo "Clone the repo and cd to it before running this script." >&2
	exit 1
fi

set -e

# Configure pull behavior

milepost "Configuring pull behavior ..."

git config branch.autosetupmerge true
git config branch.autosetuprebase always
git config lsdev.firstlinelen 75
git config lsdev.linelen 75

for BRANCH in $(git branch | cut -c3-)
do
	git config branch.$BRANCH.rebase true
done

# Add the hooks we need
milepost "Setting up hooks ..."
rm -f "$GIT_DIR"/hooks/*.sample

download hooks/commit-msg
download hooks/commit-msg.00-formatting
download hooks/pre-commit
download hooks/pre-commit.00-whitespace
download hooks/pre-commit.01-filenames

