<#
.SYNOPSIS
    Gathers what a bot-verify.yml run found into one folder for the bot to download: summary.json,
    the raw results files, and the evidence that explains each failure.

.DESCRIPTION
    The Bloom bot cannot run Bloom, so it asks bot-verify.yml to run a few tests on Windows and then
    reads the "verify-results" artifact. summary.json is what it reads first, so it has to answer
    "did the fix work" on its own, without the bot parsing JUnit or TRX:

        { label, ref, sha, passed,
          suites: [ { suite, requested, repeat, outcome, problem,
                      runs, passed, failed, failures: [ { test, error } ] } ] }

    Only the suites the run was asked for appear. `runs` counts test executions that reported a
    result, so a test repeated five times counts five. `problem` is null when the suite ran and
    reported; otherwise it says in words why there is nothing to count (a build failed, the results
    file is missing, the filter matched no test). A suite with a problem has failed even when
    `failed` is 0, because a fix cannot be proved by tests that never ran.

    Everything comes from environment variables that the workflow sets, so the script can be run
    locally against a folder of results files to check it.

    Writes "verdict=pass" or "verdict=fail" to $GITHUB_OUTPUT when that is set.
#>
param(
    # Where to put summary.json and the evidence. Created if missing.
    [Parameter(Mandatory = $true)] [string] $OutDir,
    # The repository root; results and evidence are found relative to it.
    [Parameter(Mandatory = $true)] [string] $RepoRoot
)

$ErrorActionPreference = "Stop"

# The first lines of an error are what the bot needs to tell one failure from another; a full stack
# would bury that in a JSON file it reads in one go. The whole text is still in the results files.
$kErrorLines = 15

# Playwright and vitest colour their messages, and the colour codes survive into the XML.
function Remove-Ansi([string] $text) {
    if (-not $text) { return "" }
    return [regex]::Replace($text, "\x1B\[[0-9;]*[A-Za-z]", "")
}

function Get-FirstLines([string] $text) {
    $lines = (Remove-Ansi $text).Trim() -split "\r?\n"
    return ($lines | Select-Object -First $kErrorLines) -join "`n"
}

# A new count record for one suite's results.
function New-Counts {
    return [pscustomobject]@{ Runs = 0; Passed = 0; Failed = 0; Failures = [System.Collections.Generic.List[object]]::new(); Files = 0 }
}

# Adds one JUnit file (Playwright or vitest) to $counts. Skipped test cases are not runs: a test
# that --max-failures or --bail stopped before it started proves nothing either way.
function Add-JUnit([string] $path, $counts) {
    $raw = Get-Content -LiteralPath $path -Raw
    # A zero-byte file casts to $null rather than throwing, so check before using it.
    if (-not $raw) { return }
    $xml = [xml] $raw
    $counts.Files++
    foreach ($case in $xml.SelectNodes("//testcase")) {
        if ($case.SelectSingleNode("skipped")) { continue }
        $counts.Runs++
        $problem = $case.SelectSingleNode("failure")
        if (-not $problem) { $problem = $case.SelectSingleNode("error") }
        if ($problem) {
            $counts.Failed++
            $message = $problem.GetAttribute("message")
            $body = $problem.InnerText
            # Playwright puts the whole error in the body and a short one in the message; vitest
            # puts the assertion in the message and the stack in the body. The message first,
            # then as much of the body as fits, serves both.
            $text = if ($message -and $body -and -not $body.Contains($message)) { "$message`n$body" } elseif ($body) { $body } else { $message }
            $name = $case.GetAttribute("name")
            $class = $case.GetAttribute("classname")
            $test = if ($class) { "$class > $name" } else { $name }
            $counts.Failures.Add([pscustomobject]@{ test = $test; error = (Get-FirstLines $text) })
        } else {
            $counts.Passed++
        }
    }
}

# Adds one TRX file (dotnet test) to $counts. NotExecuted is how TRX says a test was skipped.
function Add-Trx([string] $path, $counts) {
    $raw = Get-Content -LiteralPath $path -Raw
    if (-not $raw) { return }
    $xml = [xml] $raw
    $counts.Files++
    # TRX puts everything in a default namespace, so match on local names.
    foreach ($result in $xml.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $outcome = $result.GetAttribute("outcome")
        if ($outcome -eq "NotExecuted") { continue }
        $counts.Runs++
        if ($outcome -eq "Passed") {
            $counts.Passed++
            continue
        }
        $counts.Failed++
        $message = $result.SelectSingleNode(".//*[local-name()='ErrorInfo']/*[local-name()='Message']")
        $stack = $result.SelectSingleNode(".//*[local-name()='ErrorInfo']/*[local-name()='StackTrace']")
        $text = @($message, $stack) | Where-Object { $_ } | ForEach-Object { $_.InnerText }
        if (-not $text) { $text = "dotnet test reported the outcome '$outcome' with no message" }
        $counts.Failures.Add([pscustomobject]@{ test = $result.GetAttribute("testName"); error = (Get-FirstLines ($text -join "`n")) })
    }
}

# One entry of summary.json's suites list. $outcome is the outcome of the step that ran the suite
# ("success", "failure", "skipped", or empty when the step was never reached).
function New-SuiteSummary([string] $suite, [string] $requested, [string] $outcome, $counts, [string] $whyNotRun) {
    $problem = $null
    if (-not $outcome -or $outcome -eq "skipped" -or $outcome -eq "cancelled") {
        $problem = $whyNotRun
    } elseif ($counts.Files -eq 0) {
        $problem = "the suite ran but wrote no results file, so it did not finish"
    } elseif ($counts.Runs -eq 0) {
        $problem = "no test matched '$requested'"
    } elseif ($outcome -eq "failure" -and $counts.Failed -eq 0) {
        # The runner said it failed but no test did: a crashed test host, a hook that threw, or a
        # file the runner rejected. Its log says which.
        $problem = "the test runner failed although no test reported a failure; see the step's log"
    }
    return [pscustomobject][ordered]@{
        suite     = $suite
        requested = $requested
        repeat    = [int] $env:REPEAT
        outcome   = if ($outcome) { $outcome } else { "did not run" }
        problem   = $problem
        runs      = $counts.Runs
        passed    = $counts.Passed
        failed    = $counts.Failed
        failures  = @($counts.Failures)
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$resultsDir = Join-Path $RepoRoot "output/Tests/verify"

# Why a requested suite has no outcome: the builds come first, so name the one that stopped it.
$whyNotRun = "the suite did not run; see the run's log"
if ($env:FRONTEND_BUILD -ne "success") { $whyNotRun = "the front-end build did not succeed ($($env:FRONTEND_BUILD)), so the suite did not run" }
elseif ($env:CSHARP_BUILD -ne "success") { $whyNotRun = "the C# build did not succeed ($($env:CSHARP_BUILD)), so the suite did not run" }

$suites = @()

if ($env:CSHARP_FILTER) {
    $counts = New-Counts
    foreach ($file in Get-ChildItem -Path (Join-Path $resultsDir "csharp") -Filter *.trx -Recurse -ErrorAction SilentlyContinue) {
        Add-Trx $file.FullName $counts
    }
    $suites += New-SuiteSummary "csharp" $env:CSHARP_FILTER $env:CSHARP_OUTCOME $counts $whyNotRun
}

if ($env:E2E_SPECS) {
    $counts = New-Counts
    $junit = Join-Path $resultsDir "e2e-junit.xml"
    if (Test-Path -LiteralPath $junit) { Add-JUnit $junit $counts }
    $summary = New-SuiteSummary "e2e" $env:E2E_SPECS $env:E2E_OUTCOME $counts $whyNotRun
    $suites += $summary
    # The report, plus what the config and fixture keep beside each failed test (trace, screenshot,
    # Bloom's Log.txt, the collection as Bloom left it). A passing run leaves nothing worth reading.
    if ($summary.failed -gt 0 -or $summary.problem) {
        foreach ($folder in @("playwright-report", "test-results")) {
            $source = Join-Path $RepoRoot "src/BloomE2E/$folder"
            if (Test-Path -LiteralPath $source) { Copy-Item -Recurse -Force $source (Join-Path $OutDir "e2e/$folder") }
        }
    }
}

if ($env:COMPONENT_SPECS) {
    $counts = New-Counts
    $junit = Join-Path $resultsDir "component-junit.xml"
    if (Test-Path -LiteralPath $junit) { Add-JUnit $junit $counts }
    # No build feeds this suite, so a build failure is never why it did not run.
    $summary = New-SuiteSummary "component" $env:COMPONENT_SPECS $env:COMPONENT_OUTCOME $counts "the suite did not run; see the run's log"
    $suites += $summary
    # The trace the config keeps for each failed test (trace: "retain-on-failure").
    if ($summary.failed -gt 0 -or $summary.problem) {
        $source = Join-Path $RepoRoot "src/BloomBrowserUI/react_components/component-tester/test-results"
        if (Test-Path -LiteralPath $source) { Copy-Item -Recurse -Force $source (Join-Path $OutDir "component/test-results") }
    }
}

if ($env:VISUAL_SPECS) {
    $counts = New-Counts
    foreach ($file in Get-ChildItem -Path (Join-Path $resultsDir "visual") -Filter *.xml -ErrorAction SilentlyContinue) {
        Add-JUnit $file.FullName $counts
    }
    $summary = New-SuiteSummary "visual" $env:VISUAL_SPECS $env:VISUAL_OUTCOME $counts $whyNotRun
    $suites += $summary
    if ($summary.failed -gt 0 -or $summary.problem) {
        # Each mismatch leaves *-diff.png beside its *-current.png; copy only those pairs, since the
        # current image of a page that matched is a copy of a reference the bot already has.
        $inputs = Join-Path $RepoRoot "output/testing-inputs"
        $diffs = Get-ChildItem -Path $inputs -Filter *-diff.png -Recurse -ErrorAction SilentlyContinue
        foreach ($diff in $diffs) {
            $relative = [System.IO.Path]::GetRelativePath($inputs, $diff.DirectoryName)
            $dest = Join-Path $OutDir "visual/screenshots/$relative"
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -Force $diff.FullName $dest
            $current = $diff.FullName -replace "-diff\.png$", "-current.png"
            if (Test-Path -LiteralPath $current) { Copy-Item -Force $current $dest }
        }
        # The suite launches a real Bloom; when it never came up, Bloom's own log is the clue.
        $log = Join-Path $env:TEMP "SIL/Bloom"
        if ($env:TEMP -and (Test-Path -LiteralPath $log)) { Copy-Item -Recurse -Force $log (Join-Path $OutDir "visual/bloom-log") }
    }
}

# The raw results, so anything summary.json leaves out can still be read.
if (Test-Path -LiteralPath $resultsDir) { Copy-Item -Recurse -Force $resultsDir (Join-Path $OutDir "results") }

$failedSuites = @($suites | Where-Object { $_.failed -gt 0 -or $_.problem })
$passed = $suites.Count -gt 0 -and $failedSuites.Count -eq 0

$document = [ordered]@{
    label  = $env:LABEL
    ref    = $env:REF
    sha    = $env:SHA
    passed = $passed
    suites = @($suites)
}
$json = $document | ConvertTo-Json -Depth 6
# BOM-less, so a JSON parser on the bot's side does not choke on the first character.
[System.IO.File]::WriteAllText((Join-Path $OutDir "summary.json"), $json, [System.Text.UTF8Encoding]::new($false))
Write-Host $json

# A short table for a person who opens the run in the browser.
if ($env:GITHUB_STEP_SUMMARY) {
    $rows = @("### Verify: $($env:LABEL)", "", "| Suite | Requested | Runs | Passed | Failed | Problem |", "| --- | --- | --- | --- | --- | --- |")
    foreach ($s in $suites) {
        $rows += "| $($s.suite) | ``$($s.requested)`` | $($s.runs) | $($s.passed) | $($s.failed) | $($s.problem) |"
    }
    [System.IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, ($rows -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
}

if ($env:GITHUB_OUTPUT) {
    $verdict = if ($passed) { "pass" } else { "fail" }
    [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "verdict=$verdict`n", [System.Text.UTF8Encoding]::new($false))
}
