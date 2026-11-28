// Answer the plain Windows dialogs that some of Bloom's commands still show: a message box
// (MessageBox.Show) and the common Save As dialog (SaveFileDialog). They are native windows, out of
// CDP's reach, so this goes through Windows UI Automation, which reads and presses their controls
// with no pointer or focus change. The Save As dialog's file name is typed by posting characters to
// its box, which needs neither the foreground nor the focus.
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
  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  public const uint WM_CHAR = 0x0102;
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
 * Wait up to `timeoutMs` for the Save As dialog of the Bloom with process id `bloomPid` to show
 * its suggested file name, type `filePath` into its "File name:" box in its place, press Save, and
 * wait until it has closed. `filePath` must not exist yet, so that no "replace it?" box comes up,
 * and must be outside the collection folder, which Bloom refuses. `foregroundBefore` is as for
 * readAndCloseMessageBox. Returns the file name that the dialog suggested, as its box showed it
 * (Windows leaves the extension out of it where Explorer hides known extensions).
 */
export async function answerSaveAsDialog(
    bloomPid: number,
    filePath: string,
    timeoutMs: number,
    foregroundBefore?: string,
): Promise<string> {
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
    $edit = $d.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition((ById '1001'), $isEdit)))
    $save = $d.FindFirst($Scope::Children, (ById '1'))
    if ($edit -and $save) { return @{ edit = $edit; value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern); save = $save } }
  }
  return $null
}
# Type $text into the "File name:" box of $found, and wait until it holds it. The dialog keeps a
# name of its own, which follows what is typed in the box but not text that UI Automation puts
# there (SetValue): on some machines Save then saves under the name that the dialog suggested, in
# the folder that it opened in. So the box is emptied and the text posted to it a character at a
# time, as a keyboard delivers it. It is typed again if the box comes to hold anything but the
# start of $text, once it has stopped changing: until then, characters posted earlier may still be
# on their way, and would land among the new ones.
function SetName($found, $text) {
  $hwnd = [IntPtr][long]$found.edit.Current.NativeWindowHandle
  function TypeText {
    $found.value.SetValue('')
    foreach ($c in $text.ToCharArray()) {
      if (-not [Fg]::PostMessage($hwnd, [Fg]::WM_CHAR, [IntPtr][int]$c, [IntPtr]1)) {
        throw ('Windows refused a character posted to the "File name:" box (error ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error() + ').')
      }
    }
  }
  TypeText
  $before = $null
  do {
    $now = $found.value.Current.Value
    if ($now -eq $text) { return }
    if ($now -eq $before -and -not $text.StartsWith($now, [StringComparison]::Ordinal)) { TypeText; $now = $null }
    $before = $now
${kPauseBetweenScans}
  } while ((Get-Date) -lt $deadline)
  throw ('The "File name:" box did not take ' + $text + '; it holds ' + $found.value.Current.Value)
}
do {
  # The dialog shows before its controls do, and its "File name:" box shows before the dialog puts
  # its suggested name in it (Bloom always gives one), which would replace a path typed earlier. So
  # the dialog is waited on until its box holds a name.
  $found = FindSaveAs
  if ($found -and $found.value.Current.Value) {
    $suggested = $found.value.Current.Value
    SetName $found $env:BLOOM_E2E_FILE
    PressAndWaitForClose $found.save { [bool](FindSaveAs) }
    # As JSON, so that the output's trimming keeps any space at the end of the name.
    'saved'
    @{ suggested = $suggested } | ConvertTo-Json -Compress
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
    const [first, ...rest] = output.split(/\r?\n/);
    if (first === "saved")
        return (JSON.parse(rest.join("\n")) as { suggested: string }).suggested;
    if (output === "none")
        throw new Error(
            `Bloom (process ${bloomPid}) showed no Save As dialog within ${timeoutMs} ms.`,
        );
    throw new Error(
        `Bloom's dialog had no "File name:" box and Save button that UI Automation could use. ${output}`,
    );
}
