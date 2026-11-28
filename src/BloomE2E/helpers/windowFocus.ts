// Whether Bloom is the active application, as a person sees it: its window has the foreground (a
// coloured title bar, and the first click goes to a control rather than to activating the window)
// and its WebView has the keyboard (tooltips show, keys reach the page).
//
// Both are read from Windows, not from the page: Playwright turns on Chromium's focus emulation
// when it attaches, so document.hasFocus() answers true even while another application has the
// foreground, and the page gets no blur event when it loses the keyboard. Windows says which
// window has the foreground (GetForegroundWindow) and which window of that window's thread has
// the keyboard (GetGUIThreadInfo). A WebView2's own windows have class names that start with
// "Chrome_", and are children of the Bloom window that hosts them.

import { spawn, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect } from "../fixtures/bloomTest";

/** One reading of who has the foreground and the keyboard. */
export interface IFocusSample {
    /** Milliseconds since the reading started (the first sample is 0). */
    ms: number;
    /** The process that owns the foreground window; 0 when no window has the foreground. */
    foregroundPid: number;
    /** Its name, for messages. */
    foregroundProcess: string;
    /** The foreground window's handle. */
    foregroundWindow: string;
    /** The class of the window that has the keyboard, or "" when none has. */
    focusClass: string;
    /** The window that has the keyboard is the foreground window or one of its descendants. */
    focusInForegroundWindow: boolean;
}

// The PowerShell that reads one sample. $sample prints it as a line of JSON.
const kSampler = `
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class BloomFocus {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO {
    public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus;
    public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize;
    public IntPtr hwndCaret; public RECT rcCaret; }
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int p);
  [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(int thread, ref GUITHREADINFO info);
  [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static string Read() {
    IntPtr fg = GetForegroundWindow();
    int pid = 0; int thread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out pid);
    IntPtr focus = IntPtr.Zero;
    if (thread != 0) {
      var info = new GUITHREADINFO(); info.cbSize = Marshal.SizeOf(info);
      if (GetGUIThreadInfo(thread, ref info)) focus = info.hwndFocus;
    }
    var cls = new StringBuilder(256);
    if (focus != IntPtr.Zero) GetClassName(focus, cls, 256);
    bool inFg = focus != IntPtr.Zero && (focus == fg || IsChild(fg, focus));
    return pid + "|" + fg.ToInt64() + "|" + cls + "|" + (inFg ? "1" : "0");
  }
}
'@
$names = @{}
$clock = [System.Diagnostics.Stopwatch]::StartNew()
function Sample {
  $parts = [BloomFocus]::Read().Split('|')
  $p = [int]$parts[0]
  if (-not $names.ContainsKey($p)) {
    $names[$p] = if ($p) { (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName } else { '' }
  }
  @{ ms = [int]$clock.ElapsedMilliseconds; foregroundPid = $p; foregroundProcess = [string]$names[$p];
     foregroundWindow = $parts[1]; focusClass = $parts[2]; focusInForegroundWindow = ($parts[3] -eq '1') } |
    ConvertTo-Json -Compress
}
`;

function runPowerShell(
    script: string,
    env: Record<string, string>,
): ChildProcess {
    return spawn(
        "powershell",
        ["-NoProfile", "-NonInteractive", "-Command", `${kSampler}\n${script}`],
        { env: { ...process.env, ...env }, stdio: ["ignore", "pipe", "pipe"] },
    );
}

/** Collect what a PowerShell run of the sampler prints, as samples, once it ends. */
function collectSamples(child: ChildProcess): Promise<IFocusSample[]> {
    let stdout = "";
    let stderr = "";
    child.stdout!.setEncoding("utf8").on("data", (d: string) => (stdout += d));
    child.stderr!.setEncoding("utf8").on("data", (d: string) => (stderr += d));
    return new Promise<IFocusSample[]>((resolve, reject) => {
        child.on("error", reject);
        child.on("close", (code) => {
            if (code !== 0) {
                reject(
                    new Error(
                        `The focus reading exited with code ${code}:\n${stderr}`,
                    ),
                );
                return;
            }
            resolve(
                stdout
                    .split(/\r?\n/)
                    .filter((line) => line.trim())
                    .map((line) => JSON.parse(line) as IFocusSample),
            );
        });
    });
}

/** Read once who has the foreground and the keyboard. */
export async function readFocus(): Promise<IFocusSample> {
    const samples = await collectSamples(runPowerShell("Sample", {}));
    if (samples.length !== 1)
        throw new Error(
            `The focus reading printed ${samples.length} samples, not one.`,
        );
    return samples[0];
}

/**
 * True when the sample shows the Bloom with process id `bloomPid` active: its window has the
 * foreground and a WebView inside that window has the keyboard.
 */
export function isBloomActive(sample: IFocusSample, bloomPid: number): boolean {
    return (
        sample.foregroundPid === bloomPid &&
        sample.focusInForegroundWindow &&
        sample.focusClass.startsWith("Chrome_")
    );
}

/** A sample in words, for failure messages. */
export function describeFocus(sample: IFocusSample, bloomPid: number): string {
    let owner: string;
    if (sample.foregroundPid === bloomPid) owner = "Bloom";
    else if (sample.foregroundPid === 0) owner = "no window";
    else
        owner = `${sample.foregroundProcess} (process ${sample.foregroundPid})`;
    const keyboard = sample.focusClass
        ? `a ${sample.focusClass} window ${sample.focusInForegroundWindow ? "inside" : "outside"} it`
        : "no window";
    return `at ${sample.ms} ms the foreground window ${sample.foregroundWindow} was ${owner}'s, and ${keyboard} had the keyboard`;
}

/**
 * Wait up to `timeoutMs` for the Bloom with process id `bloomPid` to be the active application.
 * Fails with `what` and the last reading when it never is.
 */
export async function expectBloomActive(
    bloomPid: number,
    what: string,
    timeoutMs = 15000,
): Promise<void> {
    let last: IFocusSample | undefined;
    await expect
        .poll(
            async () => {
                last = await readFocus();
                return isBloomActive(last, bloomPid);
            },
            { timeout: timeoutMs, message: what },
        )
        .toBe(true)
        .catch((error: Error) => {
            throw new Error(
                `${error.message}\nLast reading: ${last ? describeFocus(last, bloomPid) : "none"}`,
            );
        });
}

/**
 * A reading of who has the foreground and the keyboard, taken every 50 ms until stop() is called,
 * which returns a sample for each change (and the first and last).
 */
export interface IFocusRecording {
    stop: () => Promise<IFocusSample[]>;
}

/** Start recording who has the foreground and the keyboard. */
export function startFocusRecording(): IFocusRecording {
    const stopFile = Path.join(
        os.tmpdir(),
        `bloom-e2e-focus-stop-${process.pid}-${Date.now()}`,
    );
    const child = runPowerShell(
        `
$last = ''
while ($true) {
  $stopping = Test-Path -LiteralPath $env:BLOOM_E2E_FOCUS_STOP
  $s = Sample
  $key = ($s | ConvertFrom-Json | Select-Object foregroundPid, foregroundWindow, focusClass, focusInForegroundWindow | ConvertTo-Json -Compress)
  if ($key -ne $last -or $stopping) { $s; $last = $key }
  if ($stopping) { break }
  Start-Sleep -Milliseconds 50
}`,
        { BLOOM_E2E_FOCUS_STOP: stopFile },
    );
    const samples = collectSamples(child);
    // A failure before stop() is called surfaces there.
    samples.catch(() => undefined);
    return {
        stop: async () => {
            fs.writeFileSync(stopFile, "");
            try {
                return await samples;
            } finally {
                fs.rmSync(stopFile, { force: true });
            }
        },
    };
}
