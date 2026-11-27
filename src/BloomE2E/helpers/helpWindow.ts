// Read what Bloom's help window shows. Bloom opens its help file, Bloom.chm, with the Windows
// HTML Help viewer (HelpLauncher.cs), which runs inside Bloom's own process in a native window of
// the class "HH Parent". That window is outside WebView2, so CDP cannot see it; it is read here
// through Win32 from PowerShell. Inside it the topic is an Internet Explorer document, which
// answers WM_HTML_GETOBJECT with its IHTMLDocument2: its URL names the help file and the topic,
// as "mk:@MSITStore:<folder>\Bloom.chm::/<topic>", and its title is the topic's own title.
//
// Opening help takes the foreground, so a test that uses this skips under launchWithDontDisturb().

import { execFile } from "node:child_process";
import { expect } from "@playwright/test";

/** A help window of Bloom's, with the topic it shows. */
export interface IHelpWindow {
    hwnd: number;
    visible: boolean;
    /** The help document's readyState ("complete" once the topic has loaded), or "" for none. */
    readyState: string;
    /** The document's whole URL, or "" while the window has no document yet. */
    url: string;
    /** The topic's title, as the help file gives it in the page's <title>. */
    title: string;
}

/** A topic as Bloom's help window showed it. */
export interface IHelpTopicShown {
    /** The help file's name, such as "Bloom.chm". */
    helpFile: string;
    /** The topic's path inside the help file, without a leading slash. */
    topic: string;
    /** The topic's title. */
    title: string;
}

const kReadHelpWindows = `
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class BloomHelpWindows {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
  [DllImport("oleacc.dll")] static extern int ObjectFromLresult(IntPtr lresult, ref Guid iid, IntPtr w, [MarshalAs(UnmanagedType.IDispatch)] out object o);
  static string ClassOf(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  // The help windows (class "HH Parent") of the process pid.
  public static List<IntPtr> Find(int pid) {
    var found = new List<IntPtr>();
    EnumWindows((h, l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == pid && ClassOf(h) == "HH Parent") found.Add(h);
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static bool IsVisible(IntPtr h) { return IsWindowVisible(h); }
  // The document a help window shows (an IHTMLDocument2), or null while it has none.
  public static object Document(IntPtr h) {
    IntPtr ie = IntPtr.Zero;
    EnumChildWindows(h, (c, l) => { if (ClassOf(c) == "Internet Explorer_Server") { ie = c; return false; } return true; }, IntPtr.Zero);
    if (ie == IntPtr.Zero) return null;
    IntPtr result;
    // SMTO_ABORTIFHUNG, five seconds.
    if (SendMessageTimeout(ie, RegisterWindowMessage("WM_HTML_GETOBJECT"), IntPtr.Zero, IntPtr.Zero, 2, 5000, out result) == IntPtr.Zero) return null;
    var iid = new Guid("626FC520-A41E-11CF-A731-00A0C9082637"); // IHTMLDocument
    object document;
    return ObjectFromLresult(result, ref iid, IntPtr.Zero, out document) == 0 ? document : null;
  }
  // Ask a help window to close, as its close box does (WM_CLOSE).
  public static void Close(IntPtr h) { PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); }
}
'@
$pid_ = [int]$env:BLOOM_E2E_PID
$found = @()
foreach ($h in [BloomHelpWindows]::Find($pid_)) {
  if ($env:BLOOM_E2E_CLOSE -eq '1') { [BloomHelpWindows]::Close($h); continue }
  # The document's properties are read here, through PowerShell's own COM binder: late binding
  # from the compiled C# answers DISP_E_UNKNOWNNAME for them.
  $document = [BloomHelpWindows]::Document($h)
  $found += [pscustomobject]@{
    hwnd = $h.ToInt64()
    visible = [BloomHelpWindows]::IsVisible($h)
    readyState = if ($document) { [string]$document.readyState } else { '' }
    url = if ($document) { [string]$document.url } else { '' }
    title = if ($document) { [string]$document.title } else { '' }
  }
}
ConvertTo-Json -Compress -InputObject @($found)
`;

function runPowerShell(env: Record<string, string>): Promise<string> {
    return new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", kReadHelpWindows],
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

/** Every help window of the process `pid`, with what each shows. */
export async function listHelpWindows(pid: number): Promise<IHelpWindow[]> {
    const output = await runPowerShell({ BLOOM_E2E_PID: String(pid) });
    return JSON.parse(output) as IHelpWindow[];
}

/**
 * Split a help document's URL ("mk:@MSITStore:C:\...\Bloom.chm::/Tasks/x.htm") into the help
 * file's name and the topic's path. Returns undefined for a URL of any other kind, such as the
 * viewer's own "page cannot be displayed" page.
 */
export function parseHelpUrl(
    url: string,
): { helpFile: string; topic: string } | undefined {
    const match = /^mk:@MSITStore:(.+?)::\/?(.+)$/i.exec(url);
    if (!match) return undefined;
    const helpFile = match[1].split(/[\\/]/).pop()!;
    return { helpFile, topic: decodeURIComponent(match[2]) };
}

/**
 * Close every help window of the process `pid`, as a person does with its close box, and wait
 * until none is left.
 */
export async function closeHelpWindows(pid: number): Promise<void> {
    await runPowerShell({ BLOOM_E2E_PID: String(pid), BLOOM_E2E_CLOSE: "1" });
    await expect
        .poll(async () => (await listHelpWindows(pid)).length, {
            message: "Bloom's help window did not close when asked to",
            timeout: 30000,
        })
        .toBe(0);
}

/**
 * Do `openHelp` (a click on a help button), wait until Bloom shows a help window with a topic
 * fully loaded, and return that topic. Closes the help window afterwards, so the next call sees a
 * window that its own click opened. There must be no help window open before.
 */
export async function openHelpAndReadTopic(
    pid: number,
    openHelp: () => Promise<void>,
    whatWasClicked: string,
): Promise<IHelpTopicShown> {
    expect(
        await listHelpWindows(pid),
        `a help window was already open before ${whatWasClicked}`,
    ).toEqual([]);
    await openHelp();
    let shown: IHelpWindow | undefined;
    try {
        await expect
            .poll(
                async () => {
                    shown = (await listHelpWindows(pid)).find(
                        (w) => w.visible && w.readyState === "complete",
                    );
                    return shown !== undefined;
                },
                {
                    message: `${whatWasClicked} did not open Bloom's help window on a loaded page`,
                    timeout: 30000,
                },
            )
            .toBe(true);
    } catch (error) {
        // Close whatever opened, but report the failure that names the button, not a failure
        // to close.
        await closeHelpWindows(pid).catch(() => undefined);
        throw error;
    }
    await closeHelpWindows(pid);
    const parsed = parseHelpUrl(shown!.url);
    if (!parsed)
        throw new Error(
            `${whatWasClicked} opened a help window that shows "${shown!.url}" (titled "${shown!.title}"), not a page of a help file`,
        );
    return { ...parsed, title: shown!.title };
}
