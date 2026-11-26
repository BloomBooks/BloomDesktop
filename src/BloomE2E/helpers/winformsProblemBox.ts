// Read and close a plain WinForms message box that Bloom shows before its own server is up: the
// "Bloom Problem" box it reports a problem in (so the React problem dialog that problemDialogWatcher
// handles cannot be shown), or a MessageBox about what it was asked to open. Such a box is a native
// window, out of CDP's reach, so this goes through Windows UI Automation, which reads and presses
// its controls with no pointer, keystroke or focus change.

import { execFile, execFileSync } from "node:child_process";

// PowerShell that defines FindBlooms: the Bloom.exe processes whose command line contains every
// line of $env:BLOOM_E2E_COMMAND_LINE_PARTS, compared without regard to case. A failed query throws,
// so it is never taken for "no such Bloom".
const kFindBloomsByCommandLine = `
$parts = $env:BLOOM_E2E_COMMAND_LINE_PARTS -split [char]10 | Where-Object { $_ }
function FindBlooms {
  Get-CimInstance Win32_Process -Filter "Name='Bloom.exe'" -ErrorAction Stop | Where-Object {
    $line = $_.CommandLine
    $line -and -not ($parts | Where-Object { $line.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0 })
  }
}`;

// PowerShell that registers the client-side UI Automation proxies for plain Win32 controls. A
// MessageBox is one: without them its OK button and its text are inert panes, with no name a
// button would have and no pattern to press it with. The managed UIA client does not load these
// proxies when hosted in PowerShell, and this exact shape is the one that registers them; see
// .claude/skills/run-bloom/winformsUia.ps1, which does the same.
const kRegisterWin32Proxies = `
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders -TypeDefinition @"
using System.Windows.Automation;
public static class BloomUiaProxies {
  public static string Register() {
    try {
      ClientSettings.RegisterClientSideProviders(UIAutomationClientsideProviders.UIAutomationClientSideProviders.ClientSideProviderDescriptionTable);
      return "registered";
    } catch (System.Exception e) { return "failed: " + e.Message; }
  }
}
"@
$proxyState = [BloomUiaProxies]::Register()
if ($proxyState -ne 'registered') { throw "Could not register the Win32 UI Automation proxies: $proxyState" }`;

/** What a message box said, and which Bloom showed it. */
export interface IBloomProblemBox {
    /** The process that showed the box. */
    pid: number;
    /** The box's title. */
    title: string;
    /** Every piece of text in the box, in UI Automation's order, buttons excluded. */
    texts: string[];
}

/**
 * Wait up to `timeoutMs` for a "Bloom Problem" box shown by a Bloom.exe whose command line
 * contains every one of `commandLineParts` (compared without regard to case), read what it says,
 * press its OK button and return what it said. Returns undefined when no such box appeared in time.
 *
 * The command line picks out the Bloom, rather than a pid, because the Bloom that shows the box is
 * often one that Bloom started itself (to finish a collection rename, say), which the test never
 * spawned; and so that a Bloom the developer has open is never touched.
 */
export function readAndCloseBloomProblemBox(
    commandLineParts: string[],
    timeoutMs: number,
): Promise<IBloomProblemBox | undefined> {
    return readAndCloseBloomMessageBox(
        commandLineParts,
        "Bloom Problem",
        timeoutMs,
    );
}

/**
 * Like readAndCloseBloomProblemBox, for a message box whose title is `title`. A MessageBox that
 * Bloom shows with no caption has the empty string as its title.
 */
export async function readAndCloseBloomMessageBox(
    commandLineParts: string[],
    title: string,
    timeoutMs: number,
): Promise<IBloomProblemBox | undefined> {
    const script = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
${kRegisterWin32Proxies}
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
${kFindBloomsByCommandLine}
# Windows drops an environment variable set to "", so an empty title arrives as $null.
$wanted = [string]$env:BLOOM_E2E_WINDOW_TITLE
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
do {
  foreach ($proc in (FindBlooms)) {
    $byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$proc.ProcessId)
    foreach ($window in $A::RootElement.FindAll($Scope::Children, $byPid)) {
      if ($window.Current.Name -ne $wanted) { continue }
      # Read before OK is pressed: the box is gone once it has been.
      $title = $window.Current.Name
      $texts = @()
      $buttons = @()
      foreach ($el in $window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $type = $el.Current.ControlType
        if ($type -eq $CT::Button) {
          # The window's own Close (and Minimize, Maximize) buttons sit in its title bar.
          $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
          if (-not ($parent -and $parent.Current.ControlType -eq $CT::TitleBar)) { $buttons += $el }
          continue
        }
        $value = $null
        $pattern = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { $value = $pattern.Current.Value }
        if ($value) { $texts += $value } elseif (($type -eq $CT::Text) -and $el.Current.Name) { $texts += $el.Current.Name }
      }
      # Found by what it is, not by its label, which is in Windows' language. A box with one
      # button has no other to press (a MessageBox with only OK gives it the control id IDCANCEL,
      # so that Esc closes it). Among several, OK is a MessageBox's button with the control id IDOK
      # (1), which the Win32 proxies give as its AutomationId, or the "Bloom Problem" form's
      # _acceptButton, whose control name WinForms gives as its AutomationId.
      $ok = $null
      if ($buttons.Count -eq 1) { $ok = $buttons[0] } else { $ok = @($buttons | Where-Object { $_.Current.AutomationId -in @('1', '_acceptButton') })[0] }
      if (-not $ok) { throw "The '$wanted' box of process $($proc.ProcessId) has no OK button among its $($buttons.Count) buttons: $(($buttons | ForEach-Object { $_.Current.Name + ' (' + $_.Current.AutomationId + ')' }) -join ', ')" }
      $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      @{ pid = [int]$proc.ProcessId; title = $title; texts = @($texts) } | ConvertTo-Json -Compress
      exit 0
    }
  }
  # The pause between scans of a wait that ends as soon as the box appears or the deadline
  # passes. The box belongs to another process, so there is no event to wait on instead.
  $left = ($deadline - (Get-Date)).TotalMilliseconds
  if ($left -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(500, [int]$left)) }
} while ((Get-Date) -lt $deadline)
'none'`;
    const output = await new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            {
                encoding: "utf8",
                // Passed in the environment so no path or name needs quoting for PowerShell.
                env: {
                    ...process.env,
                    BLOOM_E2E_COMMAND_LINE_PARTS: commandLineParts.join("\n"),
                    BLOOM_E2E_TIMEOUT_MS: String(timeoutMs),
                    BLOOM_E2E_WINDOW_TITLE: title,
                },
                timeout: timeoutMs + 30000,
            },
            (error, stdout, stderr) =>
                error
                    ? reject(new Error(`${error.message}\n${stderr}`))
                    : resolve(stdout.trim()),
        );
    });
    if (output === "none") return undefined;
    const box = JSON.parse(output) as IBloomProblemBox;
    // ConvertTo-Json writes a one-element array as a bare string.
    box.texts = ([] as string[]).concat(box.texts ?? []);
    return box;
}

/**
 * Kill every Bloom.exe whose command line contains all of `commandLineParts` (compared without
 * regard to case), with its child processes. For cleaning up a Bloom that the test did not spawn,
 * such as the copy that finishes a collection rename, when a test fails before that copy exits.
 * Returns once none is left; throws, naming them, when some are still running after 15 seconds.
 */
export function killBloomsWithCommandLine(commandLineParts: string[]): void {
    execFileSync(
        "powershell",
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            `${kFindBloomsByCommandLine}
foreach ($proc in (FindBlooms)) { taskkill /T /F /PID $proc.ProcessId 2>&1 | Out-Null }
$deadline = (Get-Date).AddSeconds(15)
# The pause between checks of a wait that ends as soon as the processes are gone.
while (($left = @(FindBlooms)).Count -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
if ($left.Count) { throw "Bloom still running after it was killed: pid $($left.ProcessId -join ', ')" }`,
        ],
        {
            encoding: "utf8",
            env: {
                ...process.env,
                BLOOM_E2E_COMMAND_LINE_PARTS: commandLineParts.join("\n"),
            },
            // Above the 15 seconds the script waits, so a stalled process query cannot hold the
            // test's cleanup forever.
            timeout: 30000,
        },
    );
}
