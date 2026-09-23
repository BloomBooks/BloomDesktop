#!/usr/bin/env pwsh
#
# agent-vite.ps1 -- run a Vite production build of the web UI WITHOUT colliding with a
# developer's running Bloom, Vite dev server, or watch. PowerShell twin of agent-vite.sh;
# see that script for the full rationale.
#
# Agents shouldn't run the full `pnpm build`: it wipes and repopulates the shared
# output\browser, disrupting a running Bloom / dev server. This wrapper sets BLOOM_UI_OUTDIR so
# vite.config.mts redirects the whole build into a private per-terminal tree under
# output/agent/<key>/browser. It is BUILD-ONLY (confirms the bundle compiles; a running Bloom
# cannot load these bundles) and skips the pug/LESS/markdown/static-copy steps, so it is a
# fast pure-bundle check. Front-end twin of build/agent-dotnet.ps1.
#
# Usage (no args needed; extra args are passed through to `vite build`):
#   build/agent-vite.ps1
#   build/agent-vite.ps1 --logLevel info
#
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$uiDir = Join-Path $repoRoot 'src/BloomBrowserUI'

# Stable per-terminal key: the Claude session id when present (each terminal running claude
# has its own), else this process's pid. Different keys => different scratch trees.
$key = if ($env:CLAUDE_CODE_SESSION_ID) { $env:CLAUDE_CODE_SESSION_ID } else { "shell-$PID" }
$outDir = Join-Path $repoRoot "output/agent/$key/browser"

Write-Host "[agent-vite] isolated UI build dir: $outDir"

$env:BLOOM_UI_OUTDIR = $outDir
Push-Location $uiDir
try {
    # --emptyOutDir: outDir is outside the project root, so Vite will not empty it without
    # this explicit confirmation; passing it keeps the (isolated) scratch tree clean.
    & pnpm exec vite build --emptyOutDir --logLevel warn @args
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
