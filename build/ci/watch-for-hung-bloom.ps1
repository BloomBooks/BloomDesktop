<#
.SYNOPSIS
    Background watchdog for the nightly's visual-regression step: captures managed thread stacks from a
    Bloom.exe at the two moments that explain the intermittent publishing hang (BL-16612).

.DESCRIPTION
    The nightly visual-regression suite intermittently hangs. When it does, Bloom stops writing to its
    log and every test case then times out, so the run tells us only that something stopped -- never
    what. The one thing that would identify it is what each thread was doing, and we cannot get that
    after the fact: the suite's afterAll kills Bloom with `taskkill /T /F` before any later workflow
    step could look at it. So this runs alongside the tests.

    It captures at two distinct moments, and the distinction is the whole point:

    1. ON TRIGGER -- the instant Bloom's log says the page-checks navigation gave up
       ("Failed to navigate fully..."). This is the moment that decides the open question. If the
       server's worker threads are all occupied right then, worker starvation caused the failure; if
       they are sitting idle in WaitHandle.WaitAny, starvation did not, and the navigation failed for
       some other reason (WebView2/renderer) with the pool filling up only afterwards.

    2. WHEN HUNG -- a Bloom alive far longer than a healthy run needs (~5 min healthy versus 20+ when
       hung). This is the end state. It is worth having, but on its own it cannot answer the question
       above: the wedged thread holds the API lock, so every later request piles up behind it and a
       fully-consumed pool is the inevitable consequence of ANY such hang, whatever caused it. That is
       why moment 1 exists.

    Why trigger off the log rather than just sampling stacks continuously: a `dotnet-stack report`
    starts an EventPipe session and walks every thread, which is not free. Doing that every few
    seconds through Bloom's startup could itself slow things enough to cause the 10-second navigation
    timeout we are trying to explain -- manufacturing the very failure under investigation. So the only
    frequent sampling here is `Get-Process` (a cheap counter read), and stack walks happen just twice
    around the trigger and up to three times once already hung.

    Deliberately uses dotnet-stack (text call stacks) and NOT dotnet-dump: this repo is public and
    workflow artifacts are widely downloadable, so we must not publish a memory dump of a process that
    holds CI secrets. Text stacks carry no heap contents.

    Purely observational -- it reads process state and a log file, and never signals, kills, or
    otherwise touches Bloom, so it cannot change the outcome of the run it is watching. Every failure
    is swallowed: a broken watchdog must never turn a nightly red.

.PARAMETER TriggerPattern
    The log line meaning "the page-checks navigation just gave up". Matched as a regex. Includes the DOM
    name on purpose: Bloom logs "Failed to navigate fully" from two different places (page checks in
    RemoveUnwantedContentInternal, and the separate font scan in ReportInvalidFontsAsync), and matching
    the shorter string would let the fonts path spend both captures on a moment we are not asking about.

.PARAMETER HungAfterSeconds
    How long a Bloom.exe must have been alive before we treat it as hung. Healthy visual-regression
    runs launch Bloom, do their work, and kill it inside about 5 minutes; the hangs keep it alive for
    over 20. The default of 8 minutes sits clearly between the two.

.PARAMETER EarlyPollSeconds
    Sampling interval while Bloom is young, i.e. while the trigger is still plausibly ahead of us. The
    observed failure came ~26 seconds after launch, so 30-second polling could miss the window
    entirely. Cheap: a process listing plus a log read, no stack walk.

.PARAMETER ProcessName
    Overridable so the script can be exercised locally against any .NET process.

.PARAMETER LogPath
    Overridable for the same reason. When empty, the usual Bloom log locations are probed.
#>
param(
    [string] $TriggerPattern = "Failed to navigate fully to RemoveUnwantedContentInternal",
    [int] $TriggerCaptures = 2,
    [int] $TriggerGapSeconds = 15,
    [int] $HungAfterSeconds = 480,
    [int] $Captures = 3,
    [int] $CaptureGapSeconds = 240,
    [int] $EarlyPollSeconds = 5,
    [int] $EarlyWindowSeconds = 150,
    [int] $PollSeconds = 30,
    [int] $GiveUpAfterMinutes = 75,
    [string] $ProcessName = "Bloom",
    [string] $LogPath = "",
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

# Bloom holds its log open, so read with explicit read/write sharing rather than Get-Content, which can
# fail on a locked file. Returns $null -- NOT "" -- when the log cannot be read, because the caller must
# be able to tell "unreadable this instant" from "genuinely empty". Conflating them let a momentary read
# failure look like Bloom having recreated the log, which reset the stale-line baseline and would then let
# old lines fire the trigger, spending the run's only captures on the wrong moment.
function Read-LogText([string] $path) {
    try {
        if (-not (Test-Path $path)) { return $null }
        $stream = [System.IO.File]::Open(
            $path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch {
        return $null
    }
}

# Where Bloom writes its log. The visual-regression suite launches Bloom with no special logging
# configuration, so it lands in one of the standard SIL locations; probe them in the order the
# nightly's own diagnostics step uses. Returns "" when none exists yet, which simply means Bloom has
# not started writing -- the caller treats that as "no trigger yet", never as an error.
function Find-BloomLog() {
    if ($LogPath -ne "") { return $LogPath }
    $candidates = @(
        (Join-Path $env:TEMP "SIL\Bloom\Log.txt"),
        (Join-Path $env:LOCALAPPDATA "SIL\Bloom\Log.txt"),
        (Join-Path $env:LOCALAPPDATA "Temp\SIL\Bloom\Log.txt")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return ""
}

# One stack capture. `tag` distinguishes why we took it, which matters when reading the artifact later:
# a "trigger" capture answers the causation question, a "hung" capture only describes the end state.
function Capture-Stacks([int] $pid_, [string] $tag, [int] $index, [string] $why) {
    $file = Join-Path $OutputDir "bloom-stacks-$pid_-$tag-$index.txt"
    Write-Sample "CAPTURE [$tag $index] pid=$pid_ -> $file ($why)"
    try {
        # dotnet-stack talks to the runtime's diagnostics server, which is serviced by its own thread,
        # so it still answers while the app's own threads are deadlocked -- exactly our case.
        & dotnet-stack report --process-id $pid_ *> $file
        $size = 0
        $item = Get-Item $file -ErrorAction SilentlyContinue
        if ($item) { $size = $item.Length }
        Write-Sample "  capture [$tag $index] finished (exit=$LASTEXITCODE, $size bytes)"
    } catch {
        Write-Sample "  capture [$tag $index] FAILED: $($_.Exception.Message)"
    }
}

Write-Sample "watchdog started; trigger=/$TriggerPattern/, hung after ${HungAfterSeconds}s, process=$ProcessName"

$hungShotsTaken = 0
$nextHungCaptureAt = Get-Date
$triggerShotsTaken = 0
$nextTriggerCaptureAt = Get-Date
$triggerSeen = $false
$loggedLogPath = $false
$deadline = (Get-Date).AddMinutes($GiveUpAfterMinutes)

$lastLogSize = -1
$everSawProcess = $false
# Length of the log when we started watching. Anything already in it is from before this watch began
# (an earlier step in the job, or an earlier Bloom launch) and must not fire the trigger: we care about
# the navigation failing NOW, and spending our two captures on a stale line would point them at the
# wrong moment. -1 means we have not read the log yet.
$baselineLogSize = -1

while ((Get-Date) -lt $deadline) {
    $interval = $PollSeconds
    try {
        # Read the log once per poll, not once per process. Note when its size changes: that both
        # confirms we can actually read it (a silent unreadable log would otherwise look identical to
        # a run that simply never triggered) and marks in the sample timeline when Bloom went quiet.
        $logFile = Find-BloomLog
        $logText = $null
        if ($logFile -ne "") {
            if (-not $loggedLogPath) {
                Write-Sample "watching Bloom log: $logFile"
                $loggedLogPath = $true
            }
            $logText = Read-LogText $logFile
        }
        # A read we could not complete tells us nothing: leave the baseline and the trigger alone rather
        # than drawing conclusions from an absence we know to be unreliable.
        if ($null -eq $logText) {
            if ($logFile -ne "") {
                Write-Sample "  could not read the log this poll; leaving the baseline untouched"
            }
        } else {
            if ($logText.Length -ne $lastLogSize) {
                Write-Sample "  Bloom log now $($logText.Length) bytes"
                $lastLogSize = $logText.Length
            }
            if ($baselineLogSize -lt 0) {
                $baselineLogSize = $logText.Length
                if ($baselineLogSize -gt 0) {
                    Write-Sample "  ignoring the $baselineLogSize bytes already in the log; only new lines can trigger"
                }
            }
            # Bloom may recreate its log rather than append, which makes it shorter than our baseline.
            if ($logText.Length -lt $baselineLogSize) {
                Write-Sample "  log shrank, so it was recreated; rebaselining"
                $baselineLogSize = 0
            }
        }
        # Only the text appended since we started watching, so the trigger is edge-sensitive.
        $newLogText = ""
        if ($null -ne $logText -and $baselineLogSize -ge 0 -and $logText.Length -gt $baselineLogSize) {
            $newLogText = $logText.Substring($baselineLogSize)
        }
        # Decide once per poll, not once per process, so the decision cannot depend on which process
        # we happen to be looking at.
        $triggerMatched = ($newLogText -match $TriggerPattern)
        if ($triggerMatched -and -not $triggerSeen) {
            $triggerSeen = $true
            $nextTriggerCaptureAt = Get-Date
            Write-Sample "TRIGGER matched in log: /$TriggerPattern/ -- this is the moment that decides causation"
        }
        # $interval is reset to the slow default at the top of every poll, and the early-window rule
        # below stops applying once the trigger has fired -- so without this the follow-up capture would
        # land a full slow interval later instead of TriggerGapSeconds later, blunting the whole point of
        # comparing two closely-spaced snapshots.
        # Keyed on the LATCHED $triggerSeen, not the per-poll $triggerMatched: once the alert has fired,
        # the follow-up is owed regardless of whether the line is still visible in a freshly-read log.
        if ($triggerSeen -and $triggerShotsTaken -lt $TriggerCaptures) {
            $interval = $EarlyPollSeconds
        }

        $procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)

        if ($procs.Count -eq 0) {
            Write-Sample "no $ProcessName process running"
            # Poll briskly only while waiting for it to appear, so we catch its first seconds. Once it
            # has come and gone the tests are done with it, and staying brisk would just add hundreds of
            # identical lines to the artifact for the rest of the watchdog's hour.
            if (-not $everSawProcess) {
                $interval = $EarlyPollSeconds
            }
        }

        foreach ($proc in $procs) {
            $ageSeconds = -1
            try {
                $ageSeconds = [int]((Get-Date) - $proc.StartTime).TotalSeconds
            } catch {
                Write-Sample "pid=$($proc.Id) age unavailable ($($_.Exception.Message))"
                continue
            }

            $everSawProcess = $true
            Write-Sample ("pid={0} age={1}s cpu={2} threads={3} workingSetMB={4}" -f `
                    $proc.Id, $ageSeconds, $proc.CPU, $proc.Threads.Count,
                [int]($proc.WorkingSet64 / 1MB))

            # Stay on the brisk interval while the trigger could still be ahead of us.
            if ($ageSeconds -lt $EarlyWindowSeconds -and -not $triggerSeen) {
                $interval = $EarlyPollSeconds
            }

            # --- moment 1: the navigation just gave up ---
            # Again the latch, not the per-poll match: an unreadable log or a recreated one must not
            # cancel the confirming second snapshot, which is the whole comparison this exists to make.
            if ($triggerSeen -and $triggerShotsTaken -lt $TriggerCaptures `
                    -and (Get-Date) -ge $nextTriggerCaptureAt) {
                $triggerShotsTaken++
                Capture-Stacks $proc.Id "trigger" $triggerShotsTaken "log reported the navigation gave up"
                $nextTriggerCaptureAt = (Get-Date).AddSeconds($TriggerGapSeconds)
                # A second capture a few seconds later shows whether the pool state at the trigger was
                # momentary or is the state it stays wedged in.
                $interval = [Math]::Min($TriggerGapSeconds, $EarlyPollSeconds)
            }

            # --- moment 2: alive far longer than any healthy run ---
            if ($ageSeconds -ge $HungAfterSeconds -and $hungShotsTaken -lt $Captures `
                    -and (Get-Date) -ge $nextHungCaptureAt) {
                $hungShotsTaken++
                Capture-Stacks $proc.Id "hung" $hungShotsTaken "alive ${ageSeconds}s, far past a healthy run"
                $nextHungCaptureAt = (Get-Date).AddSeconds($CaptureGapSeconds)
            }
        }
    } catch {
        Write-Sample "poll failed: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $interval
}

Write-Sample "watchdog finished after ${GiveUpAfterMinutes} minutes; trigger captures: $triggerShotsTaken, hung captures: $hungShotsTaken"
