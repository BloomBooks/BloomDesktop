#!/bin/bash
set -e

script_dir="$(cd "$(dirname "$0")" && pwd)"
cd "$script_dir/../component-tester"

# Opt out of manual Playwright specs when running automated component tests.
export PLAYWRIGHT_INCLUDE_MANUAL=0

component_prefix="../LinkTargetChooser"
component_arg_added=0

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

	if [[ "$arg" == ${component_prefix}* ]]; then
		args+=("$arg")
	elif [[ "$arg" == LinkTargetChooser/* ]]; then
		args+=("${component_prefix}/${arg#LinkTargetChooser/}")
	else
		args+=("${component_prefix}/$arg")
	fi
	component_arg_added=1
done

if [ -n "$expect_value" ]; then
	echo "Error: option requires a value." >&2
	exit 1
fi

if [ "$component_arg_added" -eq 0 ]; then
	args+=("$component_prefix")
fi

yarn test "${args[@]}"
