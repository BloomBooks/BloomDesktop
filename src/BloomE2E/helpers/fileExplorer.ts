// Read, and close, the File Explorer window that Bloom opens to show a file, as its "Show in
// Folder" commands do (ProcessExtra.ShowFileInExplorerInFront). Explorer is out of CDP's reach, so
// this asks Windows' Shell.Application, which lists every open Explorer window with the folder it
// shows and the items selected in it.

import { execFile } from "node:child_process";
import * as Path from "node:path";

/** What an Explorer window showed: its folder, and the full paths of the items selected in it. */
export interface IExplorerWindow {
    folder: string;
    selected: string[];
}

/**
 * Wait up to `timeoutMs` for a File Explorer window that shows the folder holding `filePath`, and
 * close it. Returns what the window showed once something was selected in it, or, when nothing was
 * selected by the deadline, the window with an empty selection. Fails when no Explorer window showed
 * that folder in time.
 *
 * Explorer selects the file a moment after the window opens, so this waits for a selection rather
 * than reading the first thing it sees. Any Explorer window that already showed the folder before
 * the call is counted too, so a test should use a folder that nobody has open, such as one in its
 * temporary collection.
 */
export async function closeExplorerShowing(
    filePath: string,
    timeoutMs: number,
): Promise<IExplorerWindow> {
    const script = `
$ErrorActionPreference = 'Stop'
$folder = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($env:BLOOM_E2E_FILE)).TrimEnd('\\')
$shell = New-Object -ComObject Shell.Application
function Matching {
  foreach ($w in @($shell.Windows())) {
    try { $path = $w.Document.Folder.Self.Path } catch { continue }
    if ($path -and [string]::Equals($path.TrimEnd('\\'), $folder, [StringComparison]::OrdinalIgnoreCase)) { $w }
  }
}
function Selected($w) {
  try { @($w.Document.SelectedItems() | ForEach-Object { $_.Path }) } catch { @() }
}
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
$seen = $null
do {
  foreach ($w in @(Matching)) {
    $seen = $w
    $selected = @(Selected $w)
    if ($selected.Count -gt 0) {
      $result = @{ folder = $w.Document.Folder.Self.Path; selected = $selected } | ConvertTo-Json -Compress
      $w.Quit()
      $result
      exit 0
    }
  }
  # The pause between scans of a wait that ends as soon as the window shows a selection or the
  # deadline passes. The window belongs to another process, so there is no event to wait on.
  $left = ($deadline - (Get-Date)).TotalMilliseconds
  if ($left -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(250, [int]$left)) }
} while ((Get-Date) -lt $deadline)
if ($seen) {
  $result = @{ folder = $seen.Document.Folder.Self.Path; selected = @() } | ConvertTo-Json -Compress
  $seen.Quit()
  $result
  exit 0
}
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
                    BLOOM_E2E_FILE: Path.resolve(filePath),
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
            `No File Explorer window showed ${Path.dirname(filePath)} within ${timeoutMs} ms.`,
        );
    const window = JSON.parse(output) as {
        folder: string;
        selected: string[] | string | null;
    };
    // ConvertTo-Json writes an array of one item as that item, and an empty one as null.
    const selected =
        window.selected === null
            ? []
            : ([] as string[]).concat(window.selected);
    return { folder: window.folder, selected };
}
