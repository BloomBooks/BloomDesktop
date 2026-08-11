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
# For `test` it also judges the run and prints a verdict as its last line -- see the
# "Judging a test run" comment in agent-dotnet.sh for why that is not something the
# exit code can be trusted to do on its own.
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

# For everything except `test`, dotnet's own exit code is the whole story.
if ($args.Count -eq 0 -or $args[0] -ne 'test') {
    & dotnet @args "-p:OutDir=$scratch/bin/"
    exit $LASTEXITCODE
}

$markersFile = Join-Path $PSScriptRoot 'test-abort-markers.txt'
if (-not (Test-Path $markersFile)) {
    Write-Host "[agent-dotnet] cannot find $markersFile, so a truncated test run could not be detected"
    exit 1
}

$log = Join-Path ([System.IO.Path]::GetTempPath()) "agent-dotnet-test-$PID.log"

# stderr is merged in because that is where the runtime prints the crash banners we look
# for. In Windows PowerShell that redirection wraps each stderr line in an ErrorRecord,
# which under $ErrorActionPreference='Stop' would end the script; hence Continue for the
# duration, and the stringify step so what reaches the console and the log is plain text.
$ErrorActionPreference = 'Continue'
& dotnet @args "-p:OutDir=$scratch/bin/" 2>&1 |
    ForEach-Object {
        # An ErrorRecord here is just a line dotnet wrote to stderr. Take its message rather than
        # letting PowerShell format it, which for some of them yields the useless string
        # "System.Management.Automation.RemoteException" -- and a crash banner turned into that
        # would be a marker we could no longer match.
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { [string]$_ }
    } | Tee-Object -FilePath $log
$status = $LASTEXITCODE
# $LASTEXITCODE is never assigned if dotnet could not be launched at all -- not on PATH in this
# terminal, say -- because then nothing ran to set it. That leaves $status $null, and PowerShell
# turns `exit $null` into exit code 0: this script would announce a failed run and hand its caller
# a success. Which is the exact hazard it exists to remove. (The bash twin cannot have this hole;
# PIPESTATUS[0] is always a number.)
if ($null -eq $status) { $status = 1 }
$ErrorActionPreference = 'Stop'

try {
    # Match against the output with the indentation taken off the front of every line. That is what
    # lets the markers file anchor its patterns with a plain ^ and mean the same thing here as it
    # does to grep -E in the bash twin -- writing "start of line, then optional whitespace" is the
    # one thing the two regex engines cannot be made to agree on (see the note in the markers file).
    $trimmedOutput = Get-Content $log | ForEach-Object { $_.TrimStart() }

    $abortEvidence = $null
    foreach ($pattern in Get-Content $markersFile) {
        if ($pattern.Trim() -eq '' -or $pattern.TrimStart().StartsWith('#')) { continue }
        # -CaseSensitive because grep -E in the bash twin is, and Select-String is not unless told.
        # Without it the same output could be judged aborted here and passing there -- and the
        # markers file spells "[Tt]esthost" out precisely because it expects to be matched with
        # case respected.
        $hit = $trimmedOutput | Select-String -Pattern $pattern -List -CaseSensitive
        if ($hit) { $abortEvidence = $hit.Line.Trim(); break }
    }

    $summaryHit = $trimmedOutput | Select-String -Pattern '^(Passed|Failed)!' -CaseSensitive
    $summary = if ($summaryHit) { $summaryHit[-1].Line.Trim() } else { $null }

    Write-Host ""
    if ($abortEvidence) {
        Write-Host "[agent-dotnet] *** TEST RUN ABORTED -- NOT a pass. ***"
        Write-Host "[agent-dotnet] Evidence: $abortEvidence"
        Write-Host "[agent-dotnet] Any summary above counts only the tests that ran before this, so it means nothing."
        exit 1
    }
    if ($status -ne 0) {
        $detail = if ($summary) { $summary } else { 'No summary line was printed.' }
        Write-Host "[agent-dotnet] *** TEST RUN FAILED (dotnet exited $status). *** $detail"
        exit $status
    }
    if (-not $summary) {
        # A discovery-only run has no tests to summarize, so its silence is not evidence of anything.
        if ($args -contains '--list-tests' -or $args -contains '-t') {
            Write-Host "[agent-dotnet] listed tests without running them."
            exit 0
        }
        Write-Host "[agent-dotnet] *** TEST RUN INCOMPLETE: it exited 0 but never printed a summary, so we do not know what ran. ***"
        exit 1
    }
    if ($summary -like '*Failed!*') {
        # dotnet said 0 but its own summary says otherwise; believe the summary.
        Write-Host "[agent-dotnet] *** TEST RUN FAILED. *** $summary"
        exit 1
    }
    Write-Host "[agent-dotnet] test run completed. $summary"
}
finally {
    Remove-Item $log -ErrorAction SilentlyContinue
}
