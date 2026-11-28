// Whether Bloom is the active application, as a person sees it: its window has the foreground (a
// coloured title bar, and the first click goes to a control rather than to activating the window)
// and the keyboard is somewhere inside that window (tooltips show, keys reach it).
//
// Both are read from Windows, not from the page: Playwright turns on Chromium's focus emulation
// when it attaches, so document.hasFocus() answers true even while another application has the
// foreground, and the page gets no blur event when it loses the keyboard. Windows says which
// window has the foreground (GetForegroundWindow) and which window of that window's thread has
// the keyboard (GetGUIThreadInfo), and reports every later change of either as an event
// (SetWinEventHook: EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS), so a recording misses no lapse,
// however brief. The keyboard may be on the WinForms control that hosts a WebView2 or on the
// WebView2's own window, which lives in another process; either way it is inside Bloom's window.

import { spawn, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect } from "../fixtures/bloomTest";

/** Who has the foreground and the keyboard, as of one moment. */
export interface IFocusSample {
    /** Milliseconds since the reading started. */
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

// C# that reads the state (Snapshot) or records each change of it until a file appears (Record).
// Each prints one line of JSON per state.
const kFocusReader = `
using System; using System.Collections.Generic; using System.Diagnostics; using System.IO;
using System.Runtime.InteropServices; using System.Text; using System.Threading;
public static class BloomFocus {
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO {
    public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus;
    public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize;
    public IntPtr hwndCaret; public RECT rcCaret; }
  [StructLayout(LayoutKind.Sequential)] struct MSG {
    public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
    public uint time; public int x; public int y; }
  delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int p);
  [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(int thread, ref GUITHREADINFO info);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
  [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
  [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  const uint EVENT_SYSTEM_FOREGROUND = 3, EVENT_OBJECT_FOCUS = 0x8005, WM_QUIT = 0x12, GA_ROOT = 2;
  static IntPtr _fg, _focus; static string _last = ""; static Stopwatch _clock = Stopwatch.StartNew();
  static Dictionary<int, string> _names = new Dictionary<int, string>();
  static WinEventProc _proc = OnEvent;
  static string Name(int pid) {
    string n;
    if (!_names.TryGetValue(pid, out n)) {
      try { n = pid == 0 ? "" : Process.GetProcessById(pid).ProcessName; } catch { n = "?"; }
      _names[pid] = n;
    }
    return n;
  }
  static string Esc(string s) { return s.Replace("\\\\", "\\\\\\\\").Replace("\\"", "\\\\\\""); }
  static void Emit(bool always) {
    int pid = 0; if (_fg != IntPtr.Zero) GetWindowThreadProcessId(_fg, out pid);
    var cls = new StringBuilder(256); if (_focus != IntPtr.Zero) GetClassName(_focus, cls, 256);
    IntPtr root = _focus == IntPtr.Zero ? IntPtr.Zero : GetAncestor(_focus, GA_ROOT);
    bool inFg = _fg != IntPtr.Zero && root == _fg;
    string key = pid + "|" + _fg + "|" + cls + "|" + inFg;
    if (!always && key == _last) return;
    _last = key;
    Console.Out.WriteLine("{\\"ms\\":" + _clock.ElapsedMilliseconds + ",\\"foregroundPid\\":" + pid +
      ",\\"foregroundProcess\\":\\"" + Esc(Name(pid)) + "\\",\\"foregroundWindow\\":\\"" + _fg.ToInt64() +
      "\\",\\"focusClass\\":\\"" + Esc(cls.ToString()) + "\\",\\"focusInForegroundWindow\\":" + (inFg ? "true" : "false") + "}");
    Console.Out.Flush();
  }
  static void ReadNow() {
    _fg = GetForegroundWindow(); _focus = IntPtr.Zero;
    int pid; int thread = _fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(_fg, out pid);
    if (thread != 0) {
      var info = new GUITHREADINFO(); info.cbSize = Marshal.SizeOf(info);
      if (GetGUIThreadInfo(thread, ref info)) _focus = info.hwndFocus;
    }
  }
  static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {
    if (hwnd == IntPtr.Zero) return;
    if (ev == EVENT_SYSTEM_FOREGROUND) _fg = hwnd; else _focus = hwnd;
    Emit(false);
  }
  public static void Snapshot() { ReadNow(); Emit(true); }
  public static void Record(string stopFile) {
    uint pump = GetCurrentThreadId();
    var hooks = new IntPtr[] {
      SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, 0),
      SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, _proc, 0, 0, 0) };
    foreach (var h in hooks) if (h == IntPtr.Zero) throw new Exception("SetWinEventHook failed");
    var watcher = new FileSystemWatcher(Path.GetDirectoryName(stopFile), Path.GetFileName(stopFile));
    watcher.Created += (s, e) => PostThreadMessage(pump, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    watcher.EnableRaisingEvents = true;
    ReadNow(); Emit(true);
    Console.Out.WriteLine("{\\"recording\\":true}"); Console.Out.Flush();
    if (!File.Exists(stopFile)) {
      MSG msg;
      while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
    }
    watcher.Dispose();
    foreach (var h in hooks) UnhookWinEvent(h);
    ReadNow(); Emit(true);
  }
}
`;

function runFocusReader(call: string): ChildProcess {
    return spawn(
        "powershell",
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            `$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
${kFocusReader}
'@
${call}`,
        ],
        { stdio: ["ignore", "pipe", "pipe"] },
    );
}

/** What a run of the focus reader printed, line by line, and a promise of all of it at its end. */
function collectLines(
    child: ChildProcess,
    onLine: (line: string) => void,
): Promise<string[]> {
    const lines: string[] = [];
    let pending = "";
    let stderr = "";
    child.stdout!.setEncoding("utf8").on("data", (d: string) => {
        pending += d;
        const parts = pending.split(/\r?\n/);
        pending = parts.pop()!;
        for (const line of parts.filter((l) => l.trim())) {
            lines.push(line);
            onLine(line);
        }
    });
    child.stderr!.setEncoding("utf8").on("data", (d: string) => (stderr += d));
    return new Promise<string[]>((resolve, reject) => {
        child.on("error", reject);
        child.on("close", (code) => {
            if (pending.trim()) lines.push(pending);
            if (code !== 0)
                reject(
                    new Error(
                        `The focus reader exited with code ${code}:\n${stderr}`,
                    ),
                );
            else resolve(lines);
        });
    });
}

function parseSamples(lines: string[]): IFocusSample[] {
    return lines
        .map((line) => JSON.parse(line) as IFocusSample | { recording: true })
        .filter((x): x is IFocusSample => !("recording" in x));
}

/** Read once who has the foreground and the keyboard. */
export async function readFocus(): Promise<IFocusSample> {
    const samples = parseSamples(
        await collectLines(
            runFocusReader("[BloomFocus]::Snapshot()"),
            () => undefined,
        ),
    );
    if (samples.length !== 1)
        throw new Error(
            `The focus reader printed ${samples.length} readings, not one.`,
        );
    return samples[0];
}

/**
 * True when the sample shows the Bloom with process id `bloomPid` active: its window has the
 * foreground and the keyboard is inside that window.
 */
export function isBloomActive(sample: IFocusSample, bloomPid: number): boolean {
    return sample.foregroundPid === bloomPid && sample.focusInForegroundWindow;
}

/** A sample in words, for failure messages. */
export function describeFocus(sample: IFocusSample, bloomPid: number): string {
    let owner: string;
    if (sample.foregroundPid === bloomPid) owner = "Bloom";
    else if (sample.foregroundPid === 0) owner = "no window";
    else
        owner = `${sample.foregroundProcess} (process ${sample.foregroundPid})`;
    const where = sample.focusInForegroundWindow ? "inside" : "outside";
    let keyboard = "no window";
    if (sample.focusClass)
        keyboard = `a ${sample.focusClass} window ${where} it`;
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
 * A recording of who has the foreground and the keyboard: the state when it started, every change
 * Windows reported until stop() was called, and the state then.
 */
export interface IFocusRecording {
    stop: () => Promise<IFocusSample[]>;
}

/** Start recording who has the foreground and the keyboard; resolves once it is listening. */
export async function startFocusRecording(): Promise<IFocusRecording> {
    const stopFile = Path.join(
        os.tmpdir(),
        `bloom-e2e-focus-stop-${process.pid}-${Date.now()}`,
    );
    let listening: () => void = () => undefined;
    const started = new Promise<void>((resolve) => (listening = resolve));
    const child = runFocusReader(
        `[BloomFocus]::Record('${stopFile.replace(/'/g, "''")}')`,
    );
    const lines = collectLines(child, (line) => {
        if (line.includes('"recording"')) listening();
    });
    await Promise.race([
        started,
        lines.then(() => {
            throw new Error(
                "The focus reader ended before it started recording.",
            );
        }),
    ]);
    return {
        stop: async () => {
            fs.writeFileSync(stopFile, "");
            try {
                return parseSamples(await lines);
            } finally {
                fs.rmSync(stopFile, { force: true });
            }
        },
    };
}
