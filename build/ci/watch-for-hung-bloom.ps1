<#
.SYNOPSIS
    Background watchdog for the nightly's visual-regression step: captures managed thread stacks
    from a Bloom.exe that has stopped making progress.

.DESCRIPTION
    The nightly visual-regression suite intermittently hangs (BL-16612). When it does, Bloom stops
    writing to its log and every test case then times out, so the run tells us only that something
    stopped — never what. The one thing that would identify the cause is what each thread was doing
    while it was stuck, and we cannot get that after the fact: the suite's afterAll kills Bloom with
    `taskkill /T /F` before any later workflow step could look at it.

    So this runs alongside the tests instead. It polls for a Bloom.exe that has been alive far longer
    than a healthy run needs, and captures `dotnet-stack report` for it while it is still wedged. On
    the observed failures Bloom sat stuck for roughly 24 minutes, so there is a wide window to catch.

    Deliberately uses dotnet-stack (text call stacks) and NOT dotnet-dump: this repo is public and
    workflow artifacts are widely downloadable, so we must not publish a memory dump of a process
    that holds CI secrets. Text stacks carry no heap contents.

    Purely observational — it reads process state and never signals, kills, or otherwise touches
    Bloom, so it cannot change the outcome of the run it is watching. Every failure is swallowed:
    a broken watchdog must never turn a nightly red.

.PARAMETER HungAfterSeconds
    How long a Bloom.exe must have been alive before we treat it as stuck. Healthy visual-regression
    runs launch Bloom, do their work, and kill it inside about 5 minutes; the hangs keep it alive for
    over 20. The default of 8 minutes sits clearly between the two.

.PARAMETER PollSeconds
    How often to sample. Sampling is cheap (a process listing), and the samples themselves are useful:
    a starved-but-alive server and a spinning renderer look quite different in CPU over time.

.PARAMETER Captures
    How many stack captures to take, spaced CaptureGapSeconds apart. More than one matters: identical
    stacks minutes apart prove it is genuinely stuck rather than crawling.
#>
param(
    [int] $HungAfterSeconds = 480,
    [int] $PollSeconds = 30,
    [int] $Captures = 3,
    [int] $CaptureGapSeconds = 240,
    [int] $GiveUpAfterMinutes = 75,
    [string] $OutputDir = "bloom-watchdog"
)

$ErrorActionPreference = "Continue"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$sampleLog = Join-Path $OutputDir "samples.txt"

function Write-Sample([string] $text) {
    try {
        "$(Get-Date -Format 'o')  $text" | Add-Content -Path $sampleLog -Encoding utf8
    } catch {
        # Never let logging failures stop the watchdog.
    }
}

Write-Sample "watchdog started; treating a Bloom older than ${HungAfterSeconds}s as hung"

$capturesTaken = 0
$nextCaptureAllowedAt = Get-Date
$deadline = (Get-Date).AddMinutes($GiveUpAfterMinutes)

while ((Get-Date) -lt $deadline) {
    try {
        $blooms = @(Get-Process -Name "Bloom" -ErrorAction SilentlyContinue)

        if ($blooms.Count -eq 0) {
            Write-Sample "no Bloom process running"
        }

        foreach ($bloom in $blooms) {
            # StartTime throws for processes we lack rights to query; such a process is not ours anyway.
            $ageSeconds = $null
            try {
                $ageSeconds = [int]((Get-Date) - $bloom.StartTime).TotalSeconds
            } catch {
                Write-Sample "pid=$($bloom.Id) age unavailable ($($_.Exception.Message))"
                continue
            }

            Write-Sample ("pid={0} age={1}s cpu={2} threads={3} workingSetMB={4}" -f `
                    $bloom.Id, $ageSeconds, $bloom.CPU, $bloom.Threads.Count,
                [int]($bloom.WorkingSet64 / 1MB))

            if ($ageSeconds -lt $HungAfterSeconds) { continue }
            if ($capturesTaken -ge $Captures) { continue }
            if ((Get-Date) -lt $nextCaptureAllowedAt) { continue }

            $capturesTaken++
            $stackFile = Join-Path $OutputDir "bloom-stacks-$($bloom.Id)-$capturesTaken.txt"
            Write-Sample "CAPTURE $capturesTaken/$Captures for pid=$($bloom.Id) (alive ${ageSeconds}s) -> $stackFile"
            try {
                # dotnet-stack talks to the runtime's diagnostics server, which is serviced by its own
                # thread — so it still answers while the app's own threads are deadlocked, which is
                # exactly the case we are here for.
                & dotnet-stack report --process-id $bloom.Id *> $stackFile
                Write-Sample "capture $capturesTaken finished (exit=$LASTEXITCODE, $((Get-Item $stackFile -ErrorAction SilentlyContinue).Length) bytes)"
            } catch {
                Write-Sample "capture $capturesTaken FAILED: $($_.Exception.Message)"
            }
            $nextCaptureAllowedAt = (Get-Date).AddSeconds($CaptureGapSeconds)
        }
    } catch {
        Write-Sample "poll failed: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $PollSeconds
}

Write-Sample "watchdog finished after ${GiveUpAfterMinutes} minutes; captures taken: $capturesTaken"
