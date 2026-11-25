// Drive and read the Publish tab's ePUB screen: its preview, which is Readium showing the ePUB that
// Bloom staged, and its Save... button, which writes that ePUB to a file the user names.
//
// Two of those are outside the shell document:
//
//  - The preview is Readium in an iframe (output/browser/Readium/index.html), given the staged
//    book's manifest as its `url` parameter, and Readium shows the current page of the book in an
//    iframe of its own (#page-content).
//  - Save... asks for the file in the native Save As dialog (MiscUtils.
//    GetOutputFilePathOutsideCollectionFolder), which CDP cannot see. It is answered over Windows UI
//    Automation, by the common dialog's control ids, which are the same in every UI language.

import * as fs from "node:fs";
import * as Path from "node:path";
import { execFile } from "node:child_process";
import { expect, type Frame, type Page } from "@playwright/test";
import { getUiString } from "./api";
import { readZip, type IArchiveEntry } from "./bulkBloomPub";
import { selectPublishDestination } from "./publish";

/** The ePUB screen's preview, and the folder whose ePUB it shows. */
export interface IEpubPreview {
    /** The Readium frame. */
    readium: Frame;
    /** The folder Bloom staged the ePUB in: the one holding the manifest Readium was given. */
    stagingFolder: string;
}

/**
 * Open the ePUB screen, click PREVIEW, close the "Creating ePUB..." box once Bloom says it is done,
 * and return the Readium preview, showing the book's first page.
 */
export async function previewEpub(page: Page): Promise<IEpubPreview> {
    await openEpubScreen(page);
    await page.getByTestId("Common.Preview").click();

    let readium: Frame | undefined;
    let manifestUrl = "";
    await expect
        .poll(
            () => {
                for (const frame of page.frames()) {
                    const url = new URL(frame.url(), "http://localhost/");
                    const manifest = url.searchParams.get("url");
                    if (manifest && url.pathname.includes("/Readium/")) {
                        readium = frame;
                        manifestUrl = manifest;
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: 180000,
                message: "The ePUB screen never showed its Readium preview.",
            },
        )
        .toBe(true);
    await closeEpubProgressBox(page);
    // The manifest is a localhost URL of a file in the staging folder
    // (PublishEpubApi.SetupEpubControlContent).
    const prefix = manifestUrl.match(/^https?:\/\/[^/]+\/bloom\//);
    if (!prefix)
        throw new Error(
            `The ePUB preview's manifest "${manifestUrl}" is not a localhost /bloom/ URL.`,
        );
    const stagingFolder = Path.dirname(
        decodeURIComponent(manifestUrl.substring(prefix[0].length)),
    );
    await readium!
        .locator("#page-content")
        .waitFor({ state: "attached", timeout: 30000 });
    return { readium: readium!, stagingFolder };
}

/**
 * Once Bloom has finished the job that PREVIEW or Save... started, get its "Creating ePUB..." box
 * out of the way. The box closes by itself when the job reported nothing worth reading, and
 * otherwise stays, showing a Close button, until the user closes it
 * (PublishProgressDialog's closePending); a font embedded in the ePUB, for one, is worth reading.
 * So wait until the box is either gone or done, and close it when it is done.
 */
async function closeEpubProgressBox(page: Page): Promise<void> {
    const box = page.getByRole("dialog");
    const close = box.getByRole("button", {
        name: await getUiString(page, "Common.Close", "Close"),
        exact: true,
    });
    await expect
        .poll(
            async () => {
                if ((await box.count()) === 0) return "closed";
                return (await close.isVisible()) ? "done" : "working";
            },
            {
                timeout: 180000,
                message: 'The "Creating ePUB..." box never finished.',
            },
        )
        .not.toBe("working");
    if ((await box.count()) > 0) {
        await close.click();
        await box.waitFor({ state: "detached", timeout: 30000 });
    }
}

/** The frame in which Readium shows the current page of the ePUB. */
export async function getReadiumPageFrame(readium: Frame): Promise<Frame> {
    const frame = await readium
        .locator("#page-content")
        .elementHandle()
        .then((element) => element!.contentFrame());
    if (!frame) throw new Error("Readium's #page-content has no frame.");
    return frame;
}

/**
 * The content documents of a staged or saved ePUB, in reading order: the hrefs of its package
 * document's spine, relative to the package document. `files` maps a path within the ePUB to the
 * file's text.
 */
export function getEpubSpine(files: Map<string, string>): {
    packagePath: string;
    hrefs: string[];
} {
    const packagePath = getPackagePath(files);
    const opf = files.get(packagePath)!;
    const items = new Map<string, string>();
    for (const tag of opf.match(/<item\s[^>]*>/g) ?? []) {
        items.set(attribute(tag, "id")!, attribute(tag, "href")!);
    }
    const hrefs = (opf.match(/<itemref\s[^>]*>/g) ?? []).map((tag) => {
        const href = items.get(attribute(tag, "idref")!);
        if (!href)
            throw new Error(
                `The ePUB's spine names "${attribute(tag, "idref")}", which its manifest lacks.`,
            );
        return decodeURIComponent(href);
    });
    return { packagePath, hrefs };
}

/**
 * Show, in the preview, the content document `href` (as getEpubSpine gives it) by clicking
 * Readium's next-page arrow the way a person does, and return the frame that shows it. The
 * preview must be on the first page.
 */
export async function showEpubPreviewDocument(
    preview: IEpubPreview,
    spine: string[],
    href: string,
): Promise<Frame> {
    const index = spine.indexOf(href);
    if (index < 0)
        throw new Error(
            `${href} is not in the ePUB's spine: ${spine.join(", ")}`,
        );
    const fileShown = async () =>
        decodeURIComponent(
            new URL((await getReadiumPageFrame(preview.readium)).url())
                .pathname,
        )
            .split("/")
            .pop();
    await expect
        .poll(fileShown, {
            message: "Readium did not start on the ePUB's first page.",
        })
        .toBe(Path.basename(spine[0]));
    for (let i = 1; i <= index; i++) {
        await preview.readium.locator("#right-page-btn").click();
        await expect
            .poll(fileShown, {
                message: `Readium's next-page arrow did not go to ${spine[i]}.`,
            })
            .toBe(Path.basename(spine[i]));
    }
    return getReadiumPageFrame(preview.readium);
}

/** What a <video> element in the preview has loaded and played. */
export interface IPreviewVideoState {
    /** The file the video loaded, from its currentSrc. */
    fileName: string;
    /** Whether it shows the controls a reader needs to play it. */
    controls: boolean;
    /**
     * Whether it has a frame to show: HTMLMediaElement.readyState 2 (HAVE_CURRENT_DATA) or more.
     * A video that shows as a blank space (BL-16051) never gets there.
     */
    hasFrame: boolean;
    videoWidth: number;
    videoHeight: number;
    /** The MediaError code, if it failed to load. */
    errorCode: number | null;
}

/** The state of the first <video> element of a document Readium shows. */
export async function getPreviewVideoState(
    pageFrame: Frame,
): Promise<IPreviewVideoState | null> {
    return pageFrame.evaluate(() => {
        const video = document.querySelector("video");
        if (!video) return null;
        return {
            fileName: decodeURIComponent(
                (video.currentSrc || "").split("/").pop() ?? "",
            ),
            controls: video.controls,
            hasFrame: video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA,
            videoWidth: video.videoWidth,
            videoHeight: video.videoHeight,
            errorCode: video.error?.code ?? null,
        };
    });
}

/**
 * Play the first <video> of a document Readium shows, muted (so that the browser lets a script
 * start it), and wait until its playback position has moved. Returns the position reached.
 */
export async function playPreviewVideo(pageFrame: Frame): Promise<number> {
    await pageFrame.evaluate(async () => {
        const video = document.querySelector("video")!;
        video.muted = true;
        await video.play();
    });
    let position = 0;
    await expect
        .poll(
            async () => {
                position = await pageFrame.evaluate(
                    () => document.querySelector("video")!.currentTime,
                );
                return position;
            },
            { message: "The preview's video did not play." },
        )
        .toBeGreaterThan(0);
    return position;
}

/**
 * Go to Publish > ePUB and wait for its controls. The ePUB screen has no Text Languages list for
 * openPublishDestination to wait for, and its PREVIEW button has the same test id as the BloomPUB
 * screen's, so this waits for a control only the ePUB screen has.
 */
async function openEpubScreen(page: Page): Promise<void> {
    await selectPublishDestination(page, "ePUB");
    await page
        .locator("#a11yCheckerLink")
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * Open the ePUB screen, click its Save... button, answer Bloom's Save As dialog with `filePath`, and wait
 * until Bloom is done and has written the file. Returns the bytes of the saved ePUB. `filePath`
 * must not exist yet, so that no "replace it?" box comes up, and must be outside the collection
 * folder, which Bloom refuses.
 */
export async function saveEpub(
    page: Page,
    bloomPid: number,
    filePath: string,
): Promise<Buffer> {
    if (fs.existsSync(filePath))
        throw new Error(
            `${filePath} exists already; give saveEpub a new path.`,
        );
    await openEpubScreen(page);
    await page.getByTestId("PublishTab.Save").click();
    await answerSaveAsDialog(bloomPid, filePath, 120000);
    // Bloom writes the zip after the dialog closes (EpubMaker.ZipAndSaveEpub). A zip's directory
    // is the last thing written, so the file is whole once readZip can read it.
    await expect
        .poll(
            () => {
                if (!fs.existsSync(filePath)) return "no file";
                try {
                    readZip(fs.readFileSync(filePath));
                    return "whole";
                } catch (e) {
                    return String(e);
                }
            },
            {
                timeout: 60000,
                message: `Bloom did not finish writing the ePUB at ${filePath}.`,
            },
        )
        .toBe("whole");
    await closeEpubProgressBox(page);
    return fs.readFileSync(filePath);
}

/** The files of an ePUB, by their path within it. */
export function readEpub(epub: Buffer): Map<string, IArchiveEntry> {
    return new Map(readZip(epub).map((entry) => [entry.name, entry]));
}

/** The path within an ePUB of its package document, from META-INF/container.xml. */
function getPackagePath(files: Map<string, string>): string {
    const container = files.get("META-INF/container.xml");
    if (!container) throw new Error("The ePUB has no META-INF/container.xml.");
    const rootfile = container.match(/<rootfile\s[^>]*>/)?.[0];
    const path = rootfile && attribute(rootfile, "full-path");
    if (!path || !files.has(path))
        throw new Error(
            `The ePUB's container.xml names the package document "${path}", which is not in it.`,
        );
    return path;
}

/**
 * The value of attribute `name` in one XML start tag, or undefined. Bloom writes some of an
 * ePUB's files with single quotes (container.xml) and some with double.
 */
export function attribute(tag: string, name: string): string | undefined {
    return tag.match(new RegExp(`\\s${name}=(["'])(.*?)\\1`))?.[2];
}

/**
 * Wait up to `timeoutMs` for the Save As dialog of the Bloom with process id `bloomPid`, type
 * `filePath` into its "File name:" box, press Save, and wait until it has closed.
 *
 * The dialog's controls are found by the ids every Windows common file dialog gives them: 1001 for
 * the edit box of "File name:", 1 for the Save button. Pressing a button over UI Automation lets
 * Bloom take the foreground as the dialog closes, even under --dont-disturb, so the foreground is
 * handed back to whoever had it until the dialog is gone, as winformsConfirmRecycle.ts does.
 */
async function answerSaveAsDialog(
    bloomPid: number,
    filePath: string,
    timeoutMs: number,
): Promise<void> {
    const script = `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# Without the Win32 client-side proxies, UI Automation in this process shows the dialog's edit box
# and buttons as inert Panes, with no Value or Invoke pattern. This is the shape winformsUia.ps1
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
$bloomPid = [int]$env:BLOOM_E2E_PID
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $bloomPid)
$isFileDialog = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, '#32770')
function ById($id) { New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id) }
# The dialog is owned by Bloom's main window, so UI Automation shows it inside that window.
function FindDialog {
  foreach ($top in $A::RootElement.FindAll($Scope::Children, $byPid)) {
    if ($top.Current.ClassName -eq '#32770') { return $top }
    $dialog = $top.FindFirst($Scope::Children, $isFileDialog)
    if ($dialog) { return $dialog }
  }
  return $null
}
$deadline = (Get-Date).AddMilliseconds([int]$env:BLOOM_E2E_TIMEOUT_MS)
do {
  $dialog = FindDialog
  $name = $null; $save = $null
  if ($dialog) {
    # The "File name:" ComboBox holds an Edit of the same id; only the Edit takes a value.
    $isEdit = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $name = $dialog.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition((ById '1001'), $isEdit)))
    $save = $dialog.FindFirst($Scope::Children, (ById '1'))
  }
  # The dialog shows before its controls do, so a dialog without them yet is waited on too.
  if ($name -and $save) {
    $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($env:BLOOM_E2E_FILE)
    $before = [Fg]::GetForegroundWindow()
    $beforeWasBloom = (ForegroundPid) -eq $bloomPid
    $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    function GiveBack { if (-not $beforeWasBloom -and (ForegroundPid) -eq $bloomPid) { [Fg]::SetForegroundWindow($before) | Out-Null } }
    # Bounded, so a dialog that never closes is reported below instead of hanging the test.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { GiveBack } while ($watch.ElapsedMilliseconds -lt 30000 -and (FindDialog))
    GiveBack
    if (FindDialog) { throw 'The Save As dialog was still open 30 seconds after its Save button was pressed.' }
    'saved'
    exit 0
  }
  # The pause between scans of a wait that ends as soon as the dialog appears or the deadline
  # passes. The dialog belongs to another process, so there is no event to wait on instead.
  $left = ($deadline - (Get-Date)).TotalMilliseconds
  if ($left -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(250, [int]$left)) }
} while ((Get-Date) -lt $deadline)
$dialog = FindDialog
if ($dialog) {
  # Say what the dialog has instead, so that a failure shows which control was missing.
  'controls:'
  foreach ($e in $dialog.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    '  ' + $e.Current.ControlType.ProgrammaticName + ' id=' + $e.Current.AutomationId + ' name=' + $e.Current.Name
  }
} else { 'none' }`;
    const output = await new Promise<string>((resolve, reject) => {
        execFile(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            {
                encoding: "utf8",
                // Passed in the environment so that nothing needs quoting for PowerShell.
                env: {
                    ...process.env,
                    BLOOM_E2E_PID: String(bloomPid),
                    BLOOM_E2E_FILE: filePath,
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
            `Bloom (process ${bloomPid}) showed no Save As dialog within ${timeoutMs} ms.`,
        );
    if (output !== "saved")
        throw new Error(
            `Bloom's Save As dialog showed no "File name:" box (id 1001) and Save button (id 1) within ${timeoutMs} ms. Its ${output}`,
        );
}
