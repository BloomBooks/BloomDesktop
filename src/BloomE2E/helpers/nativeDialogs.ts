// The native Windows dialogs that some of Bloom's commands show. A file, folder or Save As dialog
// never shows in a test: armFileChooser tells Bloom the path to answer it with, through
// e2e/nextFileToChoose. The plain Windows message box (MessageBox.Show) does show, and is out of
// CDP's reach, so readAndCloseMessageBox reads and closes it through Windows UI Automation, which
// presses its controls with no pointer or focus change.
//
// The box is found by the id that Windows gives every such box's text, 65535, so that nothing
// depends on the words of Bloom's UI language.

import { execFile } from "node:child_process";
import type { Page } from "@playwright/test";
import { apiPostFromNode } from "./api";

// PowerShell run before readAndCloseMessageBox's script: UI Automation with the Win32 client-side
// proxies, the foreground helpers, and FindDialogs, which lists the Bloom's #32770 dialogs, whether
// UI Automation shows them at the top level or inside the Bloom window that owns them.
const kPrelude = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# Without the Win32 client-side proxies, UI Automation in this process shows a common dialog's
# buttons as inert Panes, with no Invoke pattern. This is the shape winformsUia.ps1
# (the run-bloom skill) found to work: compiled C#, the call inside try/catch, returning a string.
# A void wrapper, or the call made from PowerShell itself, throws a NullReferenceException.
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
if ($proxyState -ne 'registered') { throw "Could not register the Win32 UI Automation proxies: $proxyState" }
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
$CT = [System.Windows.Automation.ControlType]
$bloomPid = [int]$env:BLOOM_E2E_PID
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $bloomPid)
$isDialog = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, '#32770')
function ById($id) { New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id) }
function FindDialogs {
  $found = @()
  foreach ($top in $A::RootElement.FindAll($Scope::Children, $byPid)) {
    if ($top.Current.ClassName -eq '#32770') { $found += $top }
    foreach ($d in $top.FindAll($Scope::Children, $isDialog)) { $found += $d }
  }
  return $found
}
function WindowPid($h) { $p = 0; [Fg]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null; $p }
# The window to give the foreground back to: the last window not Bloom's that had it. It starts as
# the one that had it before the command that shows the dialog was started, when the caller read it
# (getForegroundWindow), and follows the person if they switch to another program meanwhile.
$script:lastOther = [IntPtr]::Zero
function TrackForeground {
  $h = [Fg]::GetForegroundWindow()
  if ($h -ne [IntPtr]::Zero -and (WindowPid $h) -ne $bloomPid) { $script:lastOther = $h }
}
if ($env:BLOOM_E2E_FOREGROUND) {
  $given = [IntPtr][long]$env:BLOOM_E2E_FOREGROUND
  if ((WindowPid $given) -ne $bloomPid) { $script:lastOther = $given }
}
TrackForeground
# Hand the foreground back when Bloom has taken it: a dialog Bloom shows, and the press of its
# button over UI Automation, let Bloom take the foreground, even under --dont-disturb.
function GiveBack {
  TrackForeground
  if ($script:lastOther -ne [IntPtr]::Zero -and (ForegroundPid) -eq $bloomPid) { [Fg]::SetForegroundWindow($script:lastOther) | Out-Null }
}
# Press a button, then keep handing the foreground back until the dialog is gone. Bounded, so a
# dialog that never closes is reported instead of hanging.
function PressAndWaitForClose($button, $stillOpen) {
  GiveBack
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $watch = [Diagnostics.Stopwatch]::StartNew()
  do { GiveBack } while ($watch.ElapsedMilliseconds -lt 30000 -and (& $stillOpen))
  GiveBack
  if (& $stillOpen) { throw 'The dialog was still open 30 seconds after its button was pressed.' }
}
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
`;

// The pause between scans of a wait that ends as soon as the dialog appears or the deadline passes.
// The dialog belongs to another process, so there is no event to wait on instead.
const kPauseBetweenScans = `
  GiveBack
  $left = ($deadline - (Get-Date)).TotalMilliseconds
  if ($left -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(250, [int]$left)) }
`;

/** Run `script` (after kPrelude) in PowerShell with `env`, and return what it printed. */
function runUiaScript(
    script: string,
    env: Record<string, string>,
    timeoutMs: number,
): Promise<string> {
    return new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                // UTF-8 out, or a message with letters outside the console's code page reaches
                // Node garbled.
                `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n${kPrelude}\n${script}`,
            ],
            {
                encoding: "utf8",
                // Passed in the environment so that nothing needs quoting for PowerShell.
                env: {
                    ...process.env,
                    ...env,
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
}

/**
 * The window that has the foreground now, as a number for readAndCloseMessageBox. Read it
 * before starting a command that shows a message box, so that the
 * foreground can go back to that window if the dialog takes it.
 */
export async function getForegroundWindow(): Promise<string> {
    return new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                `Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Fg { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }
'@
[Fg]::GetForegroundWindow().ToInt64()`,
            ],
            { encoding: "utf8", timeout: 60000 },
            (error, stdout, stderr) =>
                error
                    ? reject(new Error(`${error.message}\n${stderr}`))
                    : resolve(stdout.trim()),
        );
    });
}

/**
 * Wait up to `timeoutMs` for a message box (MessageBox.Show) of the Bloom with process id
 * `bloomPid`, read its message, press its button and wait until it has closed. Returns the message.
 * The box must have one button, as the boxes that report the end of a command do. Fails when no
 * such box appeared in time. `foregroundBefore` (from getForegroundWindow, read before the command
 * started) is the window the foreground goes back to if Bloom takes it.
 */
export async function readAndCloseMessageBox(
    bloomPid: number,
    timeoutMs: number,
    foregroundBefore?: string,
): Promise<string> {
    const output = await runUiaScript(
        `
$isButton = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Button)
# A message box is the dialog with a text of id 65535.
function FindBox {
  foreach ($d in (FindDialogs)) {
    $text = $d.FindFirst($Scope::Children, (ById '65535'))
    if ($text) { return $d }
  }
  return $null
}
do {
  $box = FindBox
  if ($box) {
    # Read before the button is pressed: the box is gone once it has been.
    $message = $box.FindFirst($Scope::Children, (ById '65535')).Current.Name
    # The box's own buttons are its children; the title bar's Close is inside its TitleBar.
    $buttons = @($box.FindAll($Scope::Children, $isButton))
    if ($buttons.Count -ne 1) { throw "The message box has $($buttons.Count) buttons, not one: $message" }
    PressAndWaitForClose $buttons[0] { [bool](FindBox) }
    @{ message = $message } | ConvertTo-Json -Compress
    exit 0
  }
${kPauseBetweenScans}
} while ((Get-Date) -lt $deadline)
'none'`,
        {
            BLOOM_E2E_PID: String(bloomPid),
            BLOOM_E2E_FOREGROUND: foregroundBefore ?? "",
        },
        timeoutMs,
    );
    if (output === "none")
        throw new Error(
            `Bloom (process ${bloomPid}) showed no message box within ${timeoutMs} ms.`,
        );
    return (JSON.parse(output) as { message: string }).message;
}

/**
 * Tell Bloom the path the NEXT native file, folder or Save As dialog should return, instead of
 * opening a dialog no test can dismiss. One shot: it answers a single dialog and then goes back
 * to opening the real one. Available only because Bloom is running with --e2e. `page` is any of
 * Bloom's pages, for the port.
 *
 * It is sent from Node (apiPostFromNode), because a test may arm the next dialog while a modal
 * message box holds Bloom's UI thread.
 */
export function armFileChooser(page: Page, filePath: string): Promise<void> {
    // Bloom unescapes a text/plain body as it would a URL (RequestInfo.UnescapeString), which
    // turns "+" into a space and "%41" into "A". So the path goes escaped, and arrives as it is.
    return apiPostFromNode(
        page,
        "e2e/nextFileToChoose",
        encodeURIComponent(filePath),
    );
}
