// Read and change how Bloom's top-level windows are stacked on the Windows desktop: which window
// owns which, and which lies above which. Windows keeps an owned window above its owner, so a
// dialog that Bloom gives an owner cannot disappear behind Bloom's main window when the user
// switches to another program and back. None of this is visible over CDP, so it goes through the
// Win32 window manager, from PowerShell.
//
// Nothing here takes the keyboard: windows are moved in the stacking order with SWP_NOACTIVATE,
// never activated.

import { execFile } from "node:child_process";

/** One top-level window of a Bloom process. Handles are given as numbers. */
export interface IBloomWindow {
    hwnd: number;
    /** The window that owns it (GW_OWNER), or 0 for none. */
    owner: number;
    visible: boolean;
    /** True when the window is minimized. */
    minimized: boolean;
    title: string;
}

const kWindowsClass = `
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class BloomWindows {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  const uint GW_HWNDPREV = 3, GW_OWNER = 4;
  const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
  // One line per top-level window of the process: handle, owner, visible, minimized, title.
  public static string List(int pid) {
    var lines = new List<string>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == pid) {
        var title = new StringBuilder(512); GetWindowText(h, title, 512);
        lines.Add(h.ToInt64() + "\\t" + GetWindow(h, GW_OWNER).ToInt64() + "\\t" + (IsWindowVisible(h) ? 1 : 0) + "\\t" + (IsIconic(h) ? 1 : 0) + "\\t" + title.ToString().Replace("\\t", " ").Replace("\\n", " "));
      }
      return true;
    }, IntPtr.Zero);
    return string.Join("\\n", lines);
  }
  // Whether upper lies above lower in the desktop's stacking order.
  // The nearest window above hwnd that belongs to another process, or 0 when there is none.
  public static string NearestOtherAbove(long hwnd, int pid) {
    var h = GetWindow(new IntPtr(hwnd), GW_HWNDPREV);
    while (h != IntPtr.Zero) {
      int p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return h.ToInt64().ToString();
      h = GetWindow(h, GW_HWNDPREV);
    }
    return "0";
  }
  public static string IsAbove(long upper, long lower) {
    var h = GetWindow(new IntPtr(lower), GW_HWNDPREV);
    while (h != IntPtr.Zero) {
      if (h.ToInt64() == upper) return "true";
      h = GetWindow(h, GW_HWNDPREV);
    }
    return "false";
  }
  // Move a window in the stacking order without activating it: to the bottom (HWND_BOTTOM),
  // or to just above the window "other", where nothing that lay above "other" ends up below it.
  public static string Move(long hwnd, bool toBottom, long other) {
    IntPtr after;
    if (toBottom) after = new IntPtr(1);
    else {
      // SetWindowPos places a window below the one it is given, so give it the window above other
      // (or HWND_TOP when other is on top).
      after = GetWindow(new IntPtr(other), GW_HWNDPREV);
    }
    if (!SetWindowPos(new IntPtr(hwnd), after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
      return "error " + Marshal.GetLastWin32Error();
    return "ok";
  }
}
'@`;

function runPowerShell(
    body: string,
    env: Record<string, string>,
): Promise<string> {
    const script = `$ErrorActionPreference = 'Stop'\n${kWindowsClass}\n${body}`;
    return new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            {
                encoding: "utf8",
                // Passed in the environment so that nothing needs quoting for PowerShell.
                env: { ...process.env, ...env },
                timeout: 60000,
            },
            (error, stdout, stderr) =>
                error
                    ? reject(new Error(`${error.message}\n${stderr}`))
                    : resolve(stdout.trim()),
        );
    });
}

/** Every top-level window of the process `pid`, hidden ones included. */
export async function listTopLevelWindows(
    pid: number,
): Promise<IBloomWindow[]> {
    const output = await runPowerShell(
        "[BloomWindows]::List([int]$env:BLOOM_E2E_PID)",
        { BLOOM_E2E_PID: String(pid) },
    );
    return output
        .split(/\r?\n/)
        .filter((line) => line.length > 0)
        .map((line) => {
            const [hwnd, owner, visible, minimized, ...title] =
                line.split("\t");
            return {
                hwnd: Number(hwnd),
                owner: Number(owner),
                visible: visible === "1",
                minimized: minimized === "1",
                title: title.join("\t"),
            };
        });
}

/**
 * Bloom's main window: the window of the process `pid` that is showing and is not owned by
 * another of its showing windows. (.NET's Process.MainWindowHandle does not find it, because Bloom
 * gives its main window a hidden owner. Windows that Bloom's main window owns, such as the e2e
 * caption, are left out.) Call it when no dialog is open. Fails unless exactly one window fits.
 */
export async function findBloomMainWindow(pid: number): Promise<number> {
    const showing = (await listTopLevelWindows(pid)).filter((w) => w.visible);
    const roots = showing.filter(
        (w) => !showing.some((other) => other.hwnd === w.owner),
    );
    if (roots.length !== 1)
        throw new Error(
            `Expected Bloom (process ${pid}) to show exactly one window that no other of its windows owns, but it shows ${JSON.stringify(showing)}`,
        );
    return roots[0].hwnd;
}

/**
 * The nearest window above window `hwnd` on the desktop that does not belong to the process `pid`
 * (a hidden one included), or 0 when only that process's windows lie above it.
 */
export async function findNearestOtherWindowAbove(
    hwnd: number,
    pid: number,
): Promise<number> {
    const output = await runPowerShell(
        "[BloomWindows]::NearestOtherAbove([long]$env:BLOOM_E2E_HWND, [int]$env:BLOOM_E2E_PID)",
        { BLOOM_E2E_HWND: String(hwnd), BLOOM_E2E_PID: String(pid) },
    );
    return Number(output);
}

/** Whether window `upper` lies above window `lower` on the desktop. */
export async function isWindowAbove(
    upper: number,
    lower: number,
): Promise<boolean> {
    const output = await runPowerShell(
        "[BloomWindows]::IsAbove([long]$env:BLOOM_E2E_UPPER, [long]$env:BLOOM_E2E_LOWER)",
        { BLOOM_E2E_UPPER: String(upper), BLOOM_E2E_LOWER: String(lower) },
    );
    return output === "true";
}

/**
 * Move a window to the bottom of the desktop's stacking order without activating it, as switching
 * to another program leaves it below that program. Windows moves the windows it owns along with
 * it. No keystroke, no change of the keyboard focus.
 */
export async function moveWindowToBottom(hwnd: number): Promise<void> {
    await move(hwnd, "bottom", 0);
}

/**
 * Move window `hwnd` to just above window `other` without activating it, as switching back to a
 * program brings its window up past the ones that covered it. Windows moves the windows it owns
 * along with it. Only windows that lay at or below `other` end up below `hwnd`, so a window that
 * lay above `other` stays above.
 */
export async function moveWindowJustAbove(
    hwnd: number,
    other: number,
): Promise<void> {
    await move(hwnd, "above", other);
}

async function move(
    hwnd: number,
    where: "bottom" | "above",
    other: number,
): Promise<void> {
    const output = await runPowerShell(
        "[BloomWindows]::Move([long]$env:BLOOM_E2E_HWND, $env:BLOOM_E2E_WHERE -eq 'bottom', [long]$env:BLOOM_E2E_OTHER)",
        {
            BLOOM_E2E_HWND: String(hwnd),
            BLOOM_E2E_WHERE: where,
            BLOOM_E2E_OTHER: String(other),
        },
    );
    if (output !== "ok")
        throw new Error(
            `Could not move window ${hwnd} in the desktop's stack (${where} ${other}): ${output}`,
        );
}
