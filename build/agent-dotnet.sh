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
#   for the mechanism (obj is redirected there via BLOOM_AGENT_BUILD_DIR; bin/OutDir
#   and UseAppHost are the global -p: values this script appends).
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

BLOOM_AGENT_BUILD_DIR="$SCRATCH" exec dotnet "$@" \
    -p:OutDir="$SCRATCH/bin/" \
    -p:UseAppHost=false
