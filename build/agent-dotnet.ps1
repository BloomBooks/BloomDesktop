#!/usr/bin/env pwsh
#
# agent-dotnet.ps1 -- run `dotnet build`/`dotnet test` (or any dotnet command that
# builds this repo) WITHOUT colliding with a running Bloom. PowerShell twin of
# agent-dotnet.sh; see that script and Directory.Build.props for the full rationale.
#
# A Bloom.exe launched by ./go.sh (dotnet watch) locks output\<Config>\<Platform>\
# Bloom.exe and Bloom.dll while it runs, so a plain build/test fails there with MSB3027
# and you would have to stop Bloom first. This wrapper redirects the whole build
# (obj + bin) into a private per-terminal tree under output/agent/<key>/, so you can
# build and run unit tests while a Bloom keeps running and while other terminals do
# their own builds.
#
# Usage (exactly like dotnet, just build/test through this script):
#   build/agent-dotnet.ps1 test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~UrlPathStringTests"
#   build/agent-dotnet.ps1 build src/BloomExe/BloomExe.csproj
#
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Stable per-terminal key: the Claude session id when present (each terminal running
# claude has its own), else this process's pid. Different keys => different scratch
# trees => builds/test runs in different terminals never share a bin/obj.
$key = if ($env:CLAUDE_CODE_SESSION_ID) { $env:CLAUDE_CODE_SESSION_ID } else { "shell-$PID" }
$scratch = Join-Path $repoRoot "output/agent/$key"

Write-Host "[agent-dotnet] isolated build dir: $scratch"

# OutDir must be a global -p: (see Directory.Build.props). Apphost suppression used to
# be a global here too, but it has to vary per project — WebView2PdfMaker's apphost is
# the BloomPdfMaker.exe the PDF tests shell out to — so it now lives in
# Directory.Build.props, which a global property would have overridden.
$env:BLOOM_AGENT_BUILD_DIR = $scratch
& dotnet @args "-p:OutDir=$scratch/bin/"
exit $LASTEXITCODE
