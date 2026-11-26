// Answer the plain Windows dialogs that some of Bloom's commands still show: a message box
// (MessageBox.Show) and the common Save As dialog (SaveFileDialog). They are native windows, out of
// CDP's reach, so this goes through Windows UI Automation, which reads and presses their controls
// with no pointer, keystroke or focus change.
//
// Both are found by the ids that Windows gives every such dialog's controls, so that nothing
// depends on the words of Bloom's UI language: 65535 for a message box's text, 1001 for the edit
// box of "File name:" and 1 for Save.

import { execFile } from "node:child_process";
import * as fs from "node:fs";

// PowerShell shared by both: UI Automation with the Win32 client-side proxies, the foreground
// helpers, and FindDialogs, which lists the Bloom's #32770 dialogs, whether UI Automation shows them
// at the top level or inside the Bloom window that owns them.
const kPrelude = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# Without the Win32 client-side proxies, UI Automation in this process shows a common dialog's edit
# box and buttons as inert Panes, with no Value or Invoke pattern. This is the shape winformsUia.ps1
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
# The window that had the foreground before the command that shows the dialog was started, when
# the caller read it (getForegroundWindow); otherwise whatever has it when the button is pressed.
function WindowPid($h) { $p = 0; [Fg]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null; $p }
# Hand the foreground back to $before when Bloom has taken it: a dialog Bloom shows, and the press
# of its button over UI Automation, let Bloom take the foreground, even under --dont-disturb.
function GiveBack($before) {
  if ((WindowPid $before) -ne $bloomPid -and (ForegroundPid) -eq $bloomPid) { [Fg]::SetForegroundWindow($before) | Out-Null }
}
# Press a button, then keep handing the foreground back until the dialog is gone. Bounded, so a
# dialog that never closes is reported instead of hanging.
function PressAndWaitForClose($button, $stillOpen) {
  $before = if ($env:BLOOM_E2E_FOREGROUND) { [IntPtr][long]$env:BLOOM_E2E_FOREGROUND } else { [Fg]::GetForegroundWindow() }
  GiveBack $before
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $watch = [Diagnostics.Stopwatch]::StartNew()
  do { GiveBack $before } while ($watch.ElapsedMilliseconds -lt 30000 -and (& $stillOpen))
  GiveBack $before
  if (& $stillOpen) { throw 'The dialog was still open 30 seconds after its button was pressed.' }
}
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
`;

// The pause between scans of a wait that ends as soon as the dialog appears or the deadline passes.
// The dialog belongs to another process, so there is no event to wait on instead.
const kPauseBetweenScans = `
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
 * The window that has the foreground now, as a number for readAndCloseMessageBox and
 * answerSaveAsDialog. Read it before starting a command that shows a dialog, so that the
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
# A message box is the dialog with a text of id 65535; a Save As dialog has none.
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
 * Wait up to `timeoutMs` for the Save As dialog of the Bloom with process id `bloomPid`, type
 * `filePath` into its "File name:" box, press Save, and wait until it has closed. `filePath` must
 * not exist yet, so that no "replace it?" box comes up, and must be outside the collection folder,
 * which Bloom refuses. `foregroundBefore` is as for readAndCloseMessageBox.
 */
export async function answerSaveAsDialog(
    bloomPid: number,
    filePath: string,
    timeoutMs: number,
    foregroundBefore?: string,
): Promise<void> {
    if (fs.existsSync(filePath))
        throw new Error(
            `${filePath} exists already; give answerSaveAsDialog a new path.`,
        );
    const output = await runUiaScript(
        `
$isEdit = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Edit)
function FindSaveAs {
  foreach ($d in (FindDialogs)) {
    # The "File name:" ComboBox holds an Edit of the same id; only the Edit takes a value.
    $name = $d.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition((ById '1001'), $isEdit)))
    $save = $d.FindFirst($Scope::Children, (ById '1'))
    if ($name -and $save) { return @{ dialog = $d; name = $name; save = $save } }
  }
  return $null
}
do {
  # The dialog shows before its controls do, so a dialog without them yet is waited on too.
  $found = FindSaveAs
  if ($found) {
    $found.name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($env:BLOOM_E2E_FILE)
    PressAndWaitForClose $found.save { [bool](FindSaveAs) }
    'saved'
    exit 0
  }
${kPauseBetweenScans}
} while ((Get-Date) -lt $deadline)
# Say what the Bloom's dialogs have instead, so that a failure shows which control was missing.
$dialogs = @(FindDialogs)
if ($dialogs.Count) {
  'controls:'
  foreach ($d in $dialogs) {
    foreach ($e in $d.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
      '  ' + $e.Current.ControlType.ProgrammaticName + ' id=' + $e.Current.AutomationId + ' name=' + $e.Current.Name
    }
  }
} else { 'none' }`,
        {
            BLOOM_E2E_PID: String(bloomPid),
            BLOOM_E2E_FILE: filePath,
            BLOOM_E2E_FOREGROUND: foregroundBefore ?? "",
        },
        timeoutMs,
    );
    if (output === "saved") return;
    if (output === "none")
        throw new Error(
            `Bloom (process ${bloomPid}) showed no Save As dialog within ${timeoutMs} ms.`,
        );
    throw new Error(
        `Bloom's dialog had no "File name:" box and Save button that UI Automation could use. ${output}`,
    );
}
