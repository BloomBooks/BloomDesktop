// Answer the WinForms box in which Bloom asks whether to move something to the Recycle Bin
// (SIL.Windows.Forms' ConfirmRecycleDialog), which Bloom shows before it deletes a video in the
// Sign Language tool, among other things. It is a native window, out of CDP's reach, so this goes
// through Windows UI Automation, which reads and presses its controls with no pointer or keystroke.
//
// The box and its controls are found by their WinForms names, which UI Automation gives as their
// AutomationId, so that nothing depends on the words of Bloom's UI language.

import { execFile } from "node:child_process";

/**
 * Wait up to `timeoutMs` for the Recycle Bin box of the Bloom with process id `bloomPid`, press its
 * Delete button, and wait until it has closed. Returns the box's message, such as "The selected
 * video will be moved to the Recycle Bin." Fails when no such box appeared in time, or when it
 * did not close.
 *
 * Pressing a button over UI Automation lets Bloom take the foreground as the box closes, even
 * under --dont-disturb, so the foreground is handed back to whoever had it until the box is gone,
 * as pdfPrint.ts does for the print dialog.
 */
export async function confirmRecycleBox(
    bloomPid: number,
    timeoutMs: number,
): Promise<string> {
    const script = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Fg {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int p);
}
'@
function ForegroundPid { $p = 0; [Fg]::GetWindowThreadProcessId([Fg]::GetForegroundWindow(), [ref]$p) | Out-Null; $p }
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$bloomPid = [int]$env:BLOOM_E2E_PID
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $bloomPid)
function ById($id) { New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id) }
# The box is owned by Bloom's main window, so UI Automation may show it inside that window.
function FindBox {
  foreach ($top in $A::RootElement.FindAll($Scope::Children, $byPid)) {
    $box = $top.FindFirst($Scope::Subtree, (ById 'ConfirmRecycleDialog'))
    if ($box) { return $box }
  }
  return $null
}
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
do {
  $box = FindBox
  if ($box) {
    $delete = $box.FindFirst($Scope::Descendants, (ById 'deleteBtn'))
    if (-not $delete) { throw 'The Recycle Bin box has no Delete button.' }
    # Read before Delete is pressed: the box is gone once it has been.
    $label = $box.FindFirst($Scope::Descendants, (ById '_messageLabel'))
    $message = if ($label) { $label.Current.Name } else { '' }
    $before = [Fg]::GetForegroundWindow()
    $beforeWasBloom = (ForegroundPid) -eq $bloomPid
    $delete.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    function GiveBack { if (-not $beforeWasBloom -and (ForegroundPid) -eq $bloomPid) { [Fg]::SetForegroundWindow($before) | Out-Null } }
    # Bounded, so a box that never closes is reported below instead of hanging the test.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { GiveBack } while ($watch.ElapsedMilliseconds -lt 30000 -and (FindBox))
    GiveBack
    if (FindBox) { throw 'The Recycle Bin box was still open 30 seconds after its Delete button was pressed.' }
    @{ message = $message } | ConvertTo-Json -Compress
    exit 0
  }
  # The pause between scans of a wait that ends as soon as the box appears or the deadline
  # passes. The box belongs to another process, so there is no event to wait on instead.
  $left = ($deadline - (Get-Date)).TotalMilliseconds
  if ($left -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(250, [int]$left)) }
} while ((Get-Date) -lt $deadline)
'none'`;
    const output = await new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            {
                encoding: "utf8",
                // Passed in the environment so that nothing needs quoting for PowerShell.
                env: {
                    ...process.env,
                    BLOOM_E2E_PID: String(bloomPid),
                    BLOOM_E2E_TIMEOUT_MS: String(timeoutMs),
                },
                timeout: timeoutMs + 60000,
            },
            (error, stdout, stderr) =>
                error
                    ? reject(new Error(`${error.message}\n${stderr}`))
                    : resolve(stdout.trim()),
        );
    });
    if (output === "none")
        throw new Error(
            `Bloom (process ${bloomPid}) showed no box asking to move something to the Recycle Bin within ${timeoutMs} ms.`,
        );
    return (JSON.parse(output) as { message: string }).message;
}
