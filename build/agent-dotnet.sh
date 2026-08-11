#!/usr/bin/env bash
#
# agent-dotnet.sh -- run `dotnet build`/`dotnet test` (or any dotnet command that
# builds this repo) WITHOUT colliding with a running Bloom.
#
# Why this exists:
#   A Bloom.exe launched by ./go.sh (dotnet watch) locks
#   output\<Config>\<Platform>\Bloom.exe and Bloom.dll for as long as it runs. A plain
#   `dotnet build`/`dotnet test` writes there too, so it fails at the copy step with
#   MSB3027 ("being used by another process") and you have to stop Bloom first. The
#   same collision happens between two builds in separate terminals in one worktree.
#
#   This wrapper redirects the whole build (obj + bin) into a private per-terminal tree
#   under output/agent/<key>/, so you can build and run unit tests while a Bloom keeps
#   running and while other terminals do their own builds. See Directory.Build.props
#   for the mechanism (obj is redirected there via BLOOM_AGENT_BUILD_DIR; bin/OutDir is
#   the one global -p: this script appends, and apphost policy lives in that same props
#   file because it has to vary per project).
#
#   For `test` it does one more thing: it judges the run itself and prints a verdict as
#   its LAST line. See "Judging a test run" below.
#
# Usage (exactly like dotnet, just build/test through this script):
#   build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~UrlPathStringTests"
#   build/agent-dotnet.sh build src/BloomExe/BloomExe.csproj
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Stable per-terminal key. Every terminal running `claude` has its own
# CLAUDE_CODE_SESSION_ID; a plain shell falls back to its parent pid. Different keys
# => different scratch trees => builds/test runs in different terminals never share a
# bin/obj (a test host locks the assemblies it loads, which would otherwise re-create
# the cross-process collision this whole thing is avoiding).
KEY="${CLAUDE_CODE_SESSION_ID:-shell-$PPID}"

# The Windows dotnet/MSBuild need a Windows-style path; cygpath -m yields D:/foo form
# (drive letter, forward slashes) which MSBuild accepts and which avoids backslash
# escaping headaches in this script.
if command -v cygpath >/dev/null 2>&1; then
    SCRATCH="$(cygpath -m "$REPO_ROOT")/output/agent/$KEY"
else
    SCRATCH="$REPO_ROOT/output/agent/$KEY"
fi

echo "[agent-dotnet] isolated build dir: $SCRATCH" >&2

# OutDir must be a global -p: (see Directory.Build.props). Apphost suppression used to
# be a global here too, but it has to vary per project — WebView2PdfMaker's apphost is
# the BloomPdfMaker.exe the PDF tests shell out to — so it now lives in
# Directory.Build.props, which a global property would have overridden.

# For everything except `test`, dotnet's own exit code is the whole story, so get out of
# the way entirely and let it have this process.
if [ "${1:-}" != "test" ]; then
    BLOOM_AGENT_BUILD_DIR="$SCRATCH" exec dotnet "$@" \
        -p:OutDir="$SCRATCH/bin/"
fi

# ---------------------------------------------------------------------------
# Judging a test run
#
# A `dotnet test` run can stop early — Bloom's own code taking the test host down with
# it is the case that prompted this (BL-16667) — and when it does, VSTest still prints a
# summary of the tests that got as far as running. Everything that ran had passed, so
# that summary reads "Passed!". Anything that judges the run by its last lines, which is
# what a human skimming and an agent reading `| tail` both do, calls that green.
#
# There is a second way to be told a lie here, recorded in PAPERCUTS.md: a run whose own
# summary said `Failed! - Failed: 11` still arrived at the caller as exit code 0 (a pipe
# swallows the status, and callers do pipe this).
#
# So: capture the output, judge it three ways (crash markers, dotnet's exit code, the
# summary line), and print a one-line verdict LAST so it survives `| tail`. Nothing is
# hidden — the output still streams to the terminal as it happens.
# ---------------------------------------------------------------------------

MARKERS="$SCRIPT_DIR/test-abort-markers.txt"
if [ ! -f "$MARKERS" ]; then
    echo "[agent-dotnet] cannot find $MARKERS, so a truncated test run could not be detected" >&2
    exit 1
fi

LOG="$(mktemp -t agent-dotnet-test-XXXXXX)"
TRIMMED="$LOG.trimmed"
trap 'rm -f "$LOG" "$TRIMMED"' EXIT

# stderr is merged in because that is where the runtime prints the crash banners we are
# looking for. PIPESTATUS[0] rather than $? because $? here would be tee's.
set +e
BLOOM_AGENT_BUILD_DIR="$SCRATCH" dotnet "$@" -p:OutDir="$SCRATCH/bin/" 2>&1 | tee "$LOG"
STATUS=${PIPESTATUS[0]}
set -e

# Match against a copy with the indentation taken off the front of every line. That is what lets
# the markers file anchor its patterns with a plain ^ and mean the same thing here as it does to
# Select-String in the PowerShell twin -- writing "start of line, then optional whitespace" is the
# one thing the two regex engines cannot be made to agree on (see the note in the markers file).
sed 's/^[[:space:]]*//' "$LOG" >"$TRIMMED"

ABORT_EVIDENCE=""
while IFS= read -r pattern || [ -n "$pattern" ]; do
    # The repo is text=auto and Windows checkouts are autocrlf, so the markers file may well
    # arrive with CRLF line endings. A trailing carriage return left on the end of a pattern
    # would silently stop it ever matching, which is the one failure this file must not have.
    pattern="${pattern%$'\r'}"
    case "$pattern" in '' | '#'*) continue ;; esac
    match="$(grep -m1 -E "$pattern" "$TRIMMED" || true)"
    if [ -n "$match" ]; then
        ABORT_EVIDENCE="$match"
        break
    fi
done <"$MARKERS"

SUMMARY="$(grep -E "^(Passed|Failed)!" "$TRIMMED" | tail -1 || true)"

echo ""
if [ -n "$ABORT_EVIDENCE" ]; then
    echo "[agent-dotnet] *** TEST RUN ABORTED -- NOT a pass. ***"
    echo "[agent-dotnet] Evidence: $ABORT_EVIDENCE"
    echo "[agent-dotnet] Any summary above counts only the tests that ran before this, so it means nothing."
    exit 1
fi
if [ "$STATUS" -ne 0 ]; then
    echo "[agent-dotnet] *** TEST RUN FAILED (dotnet exited $STATUS). *** ${SUMMARY:-No summary line was printed.}"
    exit "$STATUS"
fi
if [ -z "$SUMMARY" ]; then
    # A discovery-only run has no tests to summarize, so its silence is not evidence of anything.
    for arg in "$@"; do
        if [ "$arg" = "--list-tests" ] || [ "$arg" = "-t" ]; then
            echo "[agent-dotnet] listed tests without running them."
            exit 0
        fi
    done
    echo "[agent-dotnet] *** TEST RUN INCOMPLETE: it exited 0 but never printed a summary, so we do not know what ran. ***"
    exit 1
fi
case "$SUMMARY" in
    *Failed!*)
        # dotnet said 0 but its own summary says otherwise; believe the summary.
        echo "[agent-dotnet] *** TEST RUN FAILED. *** $SUMMARY"
        exit 1
        ;;
esac
echo "[agent-dotnet] test run completed. $SUMMARY"
