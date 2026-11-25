// Read and close the plain WinForms "Bloom Problem" box, which Bloom shows when it reports a problem
// before its own server is up (so the React problem dialog that problemDialogWatcher handles cannot
// be shown). It is a native window, out of CDP's reach, so this goes through Windows UI Automation,
// which reads and presses its controls with no pointer, keystroke or focus change.

import { execFile } from "node:child_process";

/** What a "Bloom Problem" box said, and which Bloom showed it. */
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
export async function readAndCloseBloomProblemBox(
    commandLineParts: string[],
    timeoutMs: number,
): Promise<IBloomProblemBox | undefined> {
    const script = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$parts = $env:BLOOM_E2E_COMMAND_LINE_PARTS -split [char]10 | Where-Object { $_ }
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
do {
  $procs = Get-CimInstance Win32_Process -Filter "Name='Bloom.exe'" | Where-Object {
    $line = $_.CommandLine
    $line -and -not ($parts | Where-Object { $line.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0 })
  }
  foreach ($proc in $procs) {
    $byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$proc.ProcessId)
    foreach ($window in $A::RootElement.FindAll($Scope::Children, $byPid)) {
      if ($window.Current.Name -ne 'Bloom Problem') { continue }
      $texts = @()
      $ok = $null
      foreach ($el in $window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $type = $el.Current.ControlType
        if ($type -eq $CT::Button) {
          if ($el.Current.Name -eq 'OK') { $ok = $el }
          continue
        }
        $value = $null
        $pattern = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { $value = $pattern.Current.Value }
        if ($value) { $texts += $value } elseif (($type -eq $CT::Text) -and $el.Current.Name) { $texts += $el.Current.Name }
      }
      if (-not $ok) { throw "The Bloom Problem box of process $($proc.ProcessId) has no OK button." }
      $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      @{ pid = [int]$proc.ProcessId; title = $window.Current.Name; texts = @($texts) } | ConvertTo-Json -Compress
      exit 0
    }
  }
  Start-Sleep -Milliseconds 500
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
