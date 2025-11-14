#!/bin/bash
set -e

script_dir="$(cd "$(dirname "$0")" && pwd)"
cd "$script_dir/../component-tester"

# Opt out of manual Playwright specs when running automated component tests.
export PLAYWRIGHT_INCLUDE_MANUAL=0

args=()
expect_value=""

needs_value() {
	case "$1" in
		--config|--grep|--grep-invert|--project|--reporter|--output|--workers|--timeout|--max-failures|--repeat-each|--retries|--shard|--global-timeout)
			return 0
			;;
		-c|-g|-p|-j|-o|-w|-t)
			return 0
			;;
		*)
			return 1
			;;
	esac
}

for arg in "$@"; do
	if [ -n "$expect_value" ]; then
		args+=("$arg")
		expect_value=""
		continue
	fi

	if needs_value "$arg"; then
		args+=("$arg")
		expect_value="pending"
		continue
	fi

	if [[ "$arg" == -* ]]; then
		args+=("$arg")
		continue
	fi

	if [[ "$arg" == LinkTargetChooser/* ]]; then
		args+=("$arg")
	else
		args+=("LinkTargetChooser/$arg")
	fi
done

if [ -n "$expect_value" ]; then
	echo "Error: option requires a value." >&2
	exit 1
fi

if [ ${#args[@]} -eq 0 ]; then
	args+=("LinkTargetChooser")
fi

yarn test "${args[@]}"
