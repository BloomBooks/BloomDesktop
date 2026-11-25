// Read and close a plain WinForms MessageBox that Bloom shows, such as the "Warning" box the
// PDF & Print screen raises when it refuses to make a PDF. A MessageBox is a native window, out of
// CDP's reach, so this goes through Windows UI Automation, which reads and presses its controls
// with no pointer, keystroke or focus change. A modal box owned by the Shell is a descendant of
// the Shell's window in UI Automation, not a child of the desktop, so the search covers both.

import { execFile } from "node:child_process";

/** What a MessageBox said. */
export interface IMessageBox {
    /** The box's title. */
    title: string;
    /** Every piece of text in the box, in UI Automation's order, buttons excluded. */
    texts: string[];
}

/**
 * Wait up to `timeoutMs` for a MessageBox titled `title` in the Bloom whose process id is
 * `bloomPid`, read what it says, close it with its OK button and return what it said. Returns undefined
 * when no such box appeared in time.
 */
export async function readAndCloseMessageBox(
    bloomPid: number,
    title: string,
    timeoutMs: number,
): Promise<IMessageBox | undefined> {
    const script = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$env:BLOOM_E2E_PID)
$isWindow = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Window)
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
do {
  foreach ($top in $A::RootElement.FindAll($Scope::Children, $byPid)) {
    $candidates = @($top) + @($top.FindAll($Scope::Descendants, $isWindow))
    foreach ($window in $candidates) {
      if ($window.Current.Name -ne $env:BLOOM_E2E_TITLE) { continue }
      # Read before the box is closed: it is gone once it has been. A MessageBox shown off the UI
      # thread exposes its text and its OK button as plain Panes, so take every named element,
      # and the OK button by its name whatever its type.
      $texts = @()
      $ok = $null
      foreach ($el in $window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $name = $el.Current.Name
        if (-not $name) { continue }
        if ($name -eq 'OK') { $ok = $el; continue }
        $texts += $name
      }
      $invoke = $null
      if ($ok -and $ok.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
        $invoke.Invoke()
      } else {
        # A box with only an OK button closes the same way when its window is closed.
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
      }
      @{ title = $env:BLOOM_E2E_TITLE; texts = @($texts) } | ConvertTo-Json -Compress
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
                // Passed in the environment so no title needs quoting for PowerShell.
                env: {
                    ...process.env,
                    BLOOM_E2E_PID: String(bloomPid),
                    BLOOM_E2E_TITLE: title,
                    BLOOM_E2E_TIMEOUT_MS: String(timeoutMs),
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
    const box = JSON.parse(output) as IMessageBox;
    // ConvertTo-Json writes a one-element array as a bare string.
    box.texts = ([] as string[]).concat(box.texts ?? []);
    return box;
}
