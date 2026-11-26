// Read, and close, the File Explorer window that Bloom opens to show a file, as its "Show in
// Folder" commands do (ProcessExtra.ShowFileInExplorerInFront). Explorer is out of CDP's reach, so
// this asks Windows' Shell.Application, which lists every open Explorer window with the folder it
// shows and the items selected in it. Also read the Recycle Bin, to which Bloom moves what a person
// deletes.

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
    const output = await runPowerShell(
        script,
        {
            BLOOM_E2E_FILE: Path.resolve(filePath),
            BLOOM_E2E_TIMEOUT_MS: String(timeoutMs),
        },
        timeoutMs + 60000,
    );
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

/**
 * Whether the file that was at `filePath` went into the current Windows user's Recycle Bin at or
 * after `since`. Pass a time taken just before the delete, so that a file recycled from the same
 * path by an earlier run does not count.
 *
 * Windows keeps, for each recycled item, a small `$I` file beside the item in
 * `<drive>\$Recycle.Bin\<user's SID>`, written when the item is recycled, which records the item's
 * original path. Shell.Application shows the same items, but only by display name, which may hide
 * the extension, so this reads the `$I` files: an 8-byte version (2 since Windows 10), the size,
 * the deletion time, the path's length in characters, then the path in UTF-16. A Recycle Bin can
 * hold tens of thousands of items, so only the `$I` files written since `since` are read.
 */
export async function wasMovedToRecycleBinSince(
    filePath: string,
    since: Date,
): Promise<boolean> {
    const script = `
$ErrorActionPreference = 'Stop'
$want = [IO.Path]::GetFullPath($env:BLOOM_E2E_FILE)
$since = [DateTime]::Parse($env:BLOOM_E2E_SINCE, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$bin = Join-Path ([IO.Path]::GetPathRoot($want)) ('$Recycle.Bin\\' + $sid)
$found = $false
if (Test-Path -LiteralPath $bin) {
  # A recycled item's $I file keeps the item's extension, which narrows the listing.
  $filter = '$I*' + [IO.Path]::GetExtension($want)
  foreach ($info in @(Get-ChildItem -LiteralPath $bin -Force -Filter $filter | Where-Object { $_.LastWriteTimeUtc -ge $since })) {
    $bytes = [IO.File]::ReadAllBytes($info.FullName)
    if ([BitConverter]::ToInt64($bytes, 0) -ne 2) { continue }
    $chars = [BitConverter]::ToInt32($bytes, 24)
    $original = [Text.Encoding]::Unicode.GetString($bytes, 28, 2 * $chars).TrimEnd([char]0)
    if ([string]::Equals($original, $want, [StringComparison]::OrdinalIgnoreCase)) { $found = $true }
  }
}
if ($found) { 'yes' } else { 'no' }`;
    const output = await runPowerShell(
        script,
        {
            BLOOM_E2E_FILE: Path.resolve(filePath),
            BLOOM_E2E_SINCE: since.toISOString(),
        },
        60000,
    );
    return output === "yes";
}

/**
 * Run `script` in Windows PowerShell and return what it printed, trimmed. `env` is added to the
 * environment, so that nothing needs quoting for PowerShell. Output goes through UTF-8, so a path
 * with characters outside the machine's code page comes back whole.
 */
function runPowerShell(
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
                `[Console]::OutputEncoding = [Text.Encoding]::UTF8\n${script}`,
            ],
            {
                encoding: "utf8",
                env: { ...process.env, ...env },
                timeout: timeoutMs,
            },
            (error, stdout, stderr) =>
                error
                    ? reject(new Error(`${error.message}\n${stderr}`))
                    : resolve(stdout.trim()),
        );
    });
}
