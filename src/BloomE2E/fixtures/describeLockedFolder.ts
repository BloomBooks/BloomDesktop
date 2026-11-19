// What to say when a run's temp folder cannot be deleted after its Bloom has exited.
//
// Node reports such a failure as "EPERM, Permission denied" on the top folder, whichever file or
// folder inside it Windows refused to delete, and says nothing of what holds it. This finds the
// entries that will not go and asks Windows which processes have them open, so that the error
// names the cause.

import { execFileSync } from "node:child_process";
import * as fs from "node:fs";
import * as Path from "node:path";

/** An entry of the folder that could not be deleted, and the error code Windows gave. */
interface IUndeletableEntry {
    path: string;
    isDirectory: boolean;
    code: string;
}

/** What the PowerShell script below reports. */
interface ILockReport {
    /** For each undeletable file, the ids of the processes that have it open. */
    holders: { path: string; pids: number[]; error?: string }[];
    /** The processes that could be involved: Bloom's, WebView2's, and any that name the folder. */
    processes: {
        pid: number;
        parentPid: number;
        name: string;
        started: string;
        commandLine: string;
    }[];
}

// Asks the Restart Manager (the Windows service that installers use to find out which programs
// have a file open) about each path in the BLOOM_E2E_LOCKED_PATHS environment variable, and
// lists the processes that could be involved. It can only answer for files: Windows has no
// documented way to ask who holds a folder open.
const kLockReportScript = `
$ErrorActionPreference = 'Stop'
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class BloomE2eLocks {
  [StructLayout(LayoutKind.Sequential)]
  public struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct RM_PROCESS_INFO {
    public RM_UNIQUE_PROCESS Process;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
    public int ApplicationType; public uint AppStatus; public uint TSSessionId;
    [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
  }
  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint h, int flags, StringBuilder key);
  [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint h);
  [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint h, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, string[] services);
  [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint h, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[] info, ref uint reasons);
  public static int[] Holders(string file) {
    uint h; var key = new StringBuilder(64);
    int r = RmStartSession(out h, 0, key);
    if (r != 0) throw new Exception("RmStartSession returned " + r);
    try {
      r = RmRegisterResources(h, 1, new[] { file }, 0, IntPtr.Zero, 0, null);
      if (r != 0) throw new Exception("RmRegisterResources returned " + r);
      uint needed, count = 0, reasons = 0;
      r = RmGetList(h, out needed, ref count, null, ref reasons);
      if (needed == 0) return new int[0];
      var info = new RM_PROCESS_INFO[needed]; count = needed;
      r = RmGetList(h, out needed, ref count, info, ref reasons);
      if (r != 0) throw new Exception("RmGetList returned " + r);
      var pids = new List<int>();
      for (int i = 0; i < count; i++) pids.Add(info[i].Process.dwProcessId);
      return pids.ToArray();
    } finally { RmEndSession(h); }
  }
}
"@
$holders = @()
$paths = $env:BLOOM_E2E_LOCKED_PATHS | ConvertFrom-Json
foreach ($path in $paths) {
  if (-not $path) { continue }
  try { $holders += @{ path = $path; pids = @([BloomE2eLocks]::Holders($path)) } }
  catch { $holders += @{ path = $path; pids = @(); error = $_.Exception.Message } }
}
$folderName = $env:BLOOM_E2E_LOCKED_FOLDER_NAME
$wanted = @($holders | ForEach-Object { $_.pids })
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID } | Where-Object {
  $wanted -contains [int]$_.ProcessId -or $_.Name -like 'Bloom*' -or
  ($_.CommandLine -and ($_.CommandLine.Contains($folderName) -or $_.CommandLine.Contains('webview-exe-name=Bloom.exe')))
} | ForEach-Object {
  $commandLine = if ($_.CommandLine) { $_.CommandLine } else { '' }
  $commandLine = $commandLine -replace '\\s+', ' '
  if ($commandLine.Length -gt 400) { $commandLine = $commandLine.Substring(0, 400) + '...' }
  @{ pid = [int]$_.ProcessId; parentPid = [int]$_.ParentProcessId; name = $_.Name;
     started = $_.CreationDate.ToUniversalTime().ToString('HH:mm:ss.fff'); commandLine = $commandLine }
})
@{ holders = $holders; processes = $processes } | ConvertTo-Json -Depth 5 -Compress
`;

/**
 * Delete as much of `folder` as Windows allows, and return the entries it would not delete. A
 * folder is listed only when it is itself refused, not because something inside it was.
 */
function deleteWhatCanBeDeleted(folder: string): IUndeletableEntry[] {
    const undeletable: IUndeletableEntry[] = [];
    const visit = (path: string, isDirectory: boolean): boolean => {
        let childrenGone = true;
        if (isDirectory)
            for (const entry of fs.readdirSync(path, { withFileTypes: true }))
                if (!visit(Path.join(path, entry.name), entry.isDirectory()))
                    childrenGone = false;
        if (!childrenGone) return false;
        try {
            if (isDirectory) fs.rmdirSync(path);
            else fs.unlinkSync(path);
            return true;
        } catch (error) {
            const code = (error as NodeJS.ErrnoException).code ?? "unknown";
            if (code === "ENOENT") return true;
            undeletable.push({ path, isDirectory, code });
            return false;
        }
    };
    visit(folder, true);
    return undeletable;
}

/**
 * Say why `folder` could not be deleted: which of its files and folders Windows refuses to
 * delete, which processes have those files open, and which Bloom and WebView2 processes are
 * running. Deletes whatever it can on the way. For the message of the error that reports the
 * failed delete; it never throws.
 */
export function describeWhatHoldsFolder(folder: string): string {
    const lines: string[] = [];
    try {
        const undeletable = deleteWhatCanBeDeleted(folder);
        if (undeletable.length === 0)
            return "  A second attempt, a moment later, deleted all of it: whatever held it has let go.";
        lines.push("  Windows refuses to delete:");
        for (const entry of undeletable)
            lines.push(
                `    ${entry.path} (${entry.isDirectory ? "folder" : "file"}, ${entry.code})`,
            );

        const report = JSON.parse(
            execFileSync(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    kLockReportScript,
                ],
                {
                    encoding: "utf8",
                    timeout: 60000,
                    env: {
                        ...process.env,
                        BLOOM_E2E_LOCKED_PATHS: JSON.stringify(
                            undeletable
                                .filter((entry) => !entry.isDirectory)
                                .map((entry) => entry.path),
                        ),
                        BLOOM_E2E_LOCKED_FOLDER_NAME: Path.basename(folder),
                    },
                },
            ),
        ) as ILockReport;

        const nameOf = (pid: number) =>
            report.processes.find((p) => p.pid === pid)?.name ??
            "a process that has since exited";
        for (const holder of report.holders) {
            let heldBy = "no process, by the time we asked";
            if (holder.error) heldBy = `unknown (${holder.error})`;
            else if (holder.pids.length)
                heldBy = holder.pids
                    .map((pid) => `${nameOf(pid)} (pid ${pid})`)
                    .join(", ");
            lines.push(`  ${holder.path} is open in: ${heldBy}`);
        }
        if (undeletable.some((entry) => entry.isDirectory))
            lines.push(
                "  Windows cannot say which process holds a folder open. A process holds its " +
                    "working directory, and any folder it is watching or listing.",
            );
        lines.push(
            report.processes.length
                ? "  Bloom and WebView2 processes running now, and any other that names this folder (times are UTC):"
                : "  No Bloom or WebView2 process is running now, and no process names this folder.",
        );
        for (const p of report.processes)
            lines.push(
                `    pid ${p.pid} (parent ${p.parentPid}) ${p.name}, started ${p.started}: ${p.commandLine}`,
            );
    } catch (error) {
        lines.push(
            `  Could not find out what holds it: ${(error as Error).message}`,
        );
    }
    return lines.join("\n");
}
