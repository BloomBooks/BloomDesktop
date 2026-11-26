// Drive and read the Publish tab's PDF & Print screen: its booklet modes, its print bleed check
// box, the PDF it previews, its Print button, the browser's print dialog that button opens, and
// the window of booklet print instructions Bloom shows beside that dialog.
//
// Two of those are outside the shell document:
//
//  - The print instructions are a small WinForms window of their own (PublishPdfApi opens it), with
//    its own WebView2, so they are a separate CDP page, found by the bundle name in its title.
//  - The print dialog is the browser's own print preview. CDP cannot see it, does not capture it in
//    a screenshot, and cannot send it keys. It is reached over Windows UI Automation instead, as a
//    button named "Cancel" inside Bloom's window. While it is open, the shell document below it
//    takes no clicks (the workspace tabs, for one), so a test closes it before doing anything else.
//    See AUTOMATION-DEBT.md, "The browser's print dialog is reachable only over UI Automation".

import { execFileSync } from "node:child_process";
import { expect, type Browser, type Page } from "@playwright/test";
import { apiGet } from "./api";
import type { IPixelSize } from "./images";
import { selectPublishDestination } from "./publish";

/** The PDF & Print screen's booklet modes, by the label each one shows. */
export type PdfMode = "Simple" | "Booklet Cover" | "Booklet Insides";

/**
 * The bundle the print instructions window loads, which ReactControl.cs puts in its page's title
 * (as "ReactControl (printInstructionsBundle)", or with "Vite " before the name under a dev server).
 */
const kPrintInstructionsBundle = "printInstructionsBundle";

/**
 * Open Publish > PDF & Print and choose one of its booklet modes, the way a person does: a click on
 * the mode's button. Returns when Bloom has made the PDF and the Print button can be used.
 *
 * It must be the first mode chosen since the screen opened: the Print button is enabled once any
 * PDF is showing, so after a second choice it would not say whether the new PDF is ready.
 */
export async function choosePdfMode(page: Page, mode: PdfMode): Promise<void> {
    await selectPublishDestination(page, "PDF & Print");
    const printButton = page.getByRole("button", { name: "Print..." });
    await printButton.waitFor({ state: "visible", timeout: 30000 });
    expect(
        await printButton.isEnabled(),
        "The Print button was already enabled before any mode was chosen, so it cannot tell when the PDF is ready.",
    ).toBe(false);
    await page.getByText(mode, { exact: true }).click();
    await expect(
        printButton,
        `Bloom never finished the ${mode} PDF.`,
    ).toBeEnabled({
        timeout: 120000,
    });
}

/**
 * Open Publish > PDF & Print and tick or untick "Exclude print bleed around page edges", the way a
 * person does, and return once Bloom has the choice. Unticked, the PDF of a book that uses full
 * bleed is made with its bleed, on paper larger than the page; ticked, which is how a book starts,
 * it is made at the size of the page.
 *
 * Bloom offers the box only for a book that uses full bleed (Book and Page Settings > Print
 * Publishing), so this fails for any other book. Call it before choosePdfMode(), which needs to be
 * the first thing to make a PDF on the screen.
 */
export async function setExcludePrintBleed(
    page: Page,
    exclude: boolean,
): Promise<void> {
    await selectPublishDestination(page, "PDF & Print");
    const checkbox = page.getByRole("checkbox", {
        name: "Exclude print bleed around page edges",
    });
    await checkbox.waitFor({ state: "visible", timeout: 30000 });
    // The box is disabled until the screen has heard from Bloom that the book uses full bleed.
    await expect(
        checkbox,
        'Bloom does not offer "Exclude print bleed around page edges", which it does only for a book that uses full bleed.',
    ).toBeEnabled({ timeout: 30000 });
    // The box shows as unticked until the screen has heard from Bloom what the book has, so wait
    // for it to show that before deciding whether it needs a click.
    const excludedNow =
        (await apiGet(page, "publish/pdf/excludePrintBleed")).body === "true";
    await expect(
        checkbox,
        'The "Exclude print bleed around page edges" box never showed what Bloom has for the book.',
    ).toBeChecked({ checked: excludedNow, timeout: 30000 });
    if (excludedNow !== exclude) await checkbox.click();
    await expect(checkbox).toBeChecked({ checked: exclude });
    await expect
        .poll(
            async () =>
                (await apiGet(page, "publish/pdf/excludePrintBleed")).body,
            {
                message: `Bloom never took "Exclude print bleed around page edges" being ${exclude ? "ticked" : "unticked"}.`,
            },
        )
        .toBe(String(exclude));
}

/**
 * The PDF that the PDF & Print screen is previewing, as text with one character for each byte of
 * the file. Bloom writes the objects that describe a PDF's pages uncompressed, so they can be read
 * from this text.
 */
async function readPreviewedPdf(page: Page): Promise<string> {
    const preview = page.locator('iframe[src*="pdf" i]');
    await expect(
        preview,
        "The PDF & Print screen is not previewing a PDF.",
    ).toHaveCount(1, { timeout: 30000 });
    const src = await preview.getAttribute("src");
    const result = await page.evaluate(async (url) => {
        const response = await fetch(url!);
        const bytes = new Uint8Array(await response.arrayBuffer());
        let text = "";
        for (let i = 0; i < bytes.length; i += 0x8000)
            text += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        return { status: response.status, text };
    }, src);
    if (result.status !== 200)
        throw new Error(
            `Reading the previewed PDF (${src}) gave status ${result.status}.`,
        );
    return result.text;
}

/**
 * The object that describes each page of a PDF (its "page object"), as the text of the object.
 * They come in the order the file holds them, which need not be the order of the pages.
 */
function getPdfPageObjects(pdf: string): string[] {
    const pageObjects: string[] = [];
    for (const match of pdf.matchAll(/\/Type\s*\/Page(?![a-zA-Z])/g)) {
        const start = pdf.lastIndexOf(" obj", match.index);
        const end = pdf.indexOf("endobj", match.index);
        if (start < 0 || end < 0)
            throw new Error(
                "A page of the previewed PDF is not inside an object, so its boxes cannot be read.",
            );
        pageObjects.push(pdf.slice(start, end));
    }
    if (pageObjects.length === 0)
        throw new Error("No pages could be read from the previewed PDF.");
    return pageObjects;
}

/**
 * How many pages the PDF that the PDF & Print screen is previewing has, read from the PDF file
 * itself (its page objects). Call it after choosePdfMode().
 */
export async function getPdfPreviewPageCount(page: Page): Promise<number> {
    return getPdfPageObjects(await readPreviewedPdf(page)).length;
}

/**
 * The text of the object numbered `number` in a PDF, from its number up to the data of its stream
 * if it has one, or else to its end. Throws when the file has no such object.
 */
function readPdfObject(pdf: string, number: string): string {
    const start = new RegExp(`(?:^|[\\r\\n])${number}\\s+\\d+\\s+obj`).exec(
        pdf,
    );
    if (!start) throw new Error(`The previewed PDF has no object ${number}.`);
    const ends = [
        pdf.indexOf("endobj", start.index),
        pdf.indexOf("stream", start.index),
    ].filter((end) => end >= 0);
    return pdf.slice(start.index, Math.min(...ends));
}

/** The numbers of the objects that a piece of a PDF refers to, e.g. "21" for "21 0 R", in order. */
function readPdfReferences(text: string): string[] {
    return [...text.matchAll(/(\d+)\s+\d+\s+R(?![a-zA-Z])/g)].map(
        (match) => match[1],
    );
}

/**
 * The page objects of a PDF in the order of its pages, read from its page tree: the catalog names
 * the root of the tree, and each node lists its children (Kids), which are pages or further nodes.
 */
function getPdfPageObjectsInPageOrder(pdf: string): string[] {
    const catalog = /\/Type\s*\/Catalog(?![a-zA-Z])/.exec(pdf);
    const root =
        catalog &&
        /\/Pages\s+(\d+)\s+\d+\s+R/.exec(
            pdf.slice(
                pdf.lastIndexOf(" obj", catalog.index),
                pdf.indexOf("endobj", catalog.index),
            ),
        );
    if (!root)
        throw new Error(
            "The previewed PDF's catalog does not name its page tree, so the order of its pages cannot be read.",
        );
    const pagesUnder = (number: string): string[] => {
        const node = readPdfObject(pdf, number);
        if (!/\/Type\s*\/Pages(?![a-zA-Z])/.test(node)) return [node];
        const kids = /\/Kids\s*\[([^\]]*)\]/.exec(node);
        if (!kids)
            throw new Error(
                `A node of the previewed PDF's page tree lists no pages: ${node}`,
            );
        return readPdfReferences(kids[1]).flatMap(pagesUnder);
    };
    return pagesUnder(root[1]);
}

/**
 * The size in pixels of each picture drawn on each page of the PDF that the PDF & Print screen is
 * previewing, read from the PDF file itself: one list for each page, in the order of the PDF's
 * pages. Call it after choosePdfMode().
 *
 * Bloom's PDF maker puts a picture into the PDF at the size of its file, so a picture of a known
 * size that is listed for a page was found and drawn on that page when the PDF was made, and one
 * that is missing was not. A picture with transparency is listed twice, because a PDF holds its
 * transparency as a second image of the same size.
 */
export async function getPdfPreviewPicturesByPage(
    page: Page,
): Promise<IPixelSize[][]> {
    const pdf = await readPreviewedPdf(page);
    return getPdfPageObjectsInPageOrder(pdf).map((pageObject) => {
        // What a page draws with is in its resources, which the page object holds or refers to.
        const elsewhere = /\/Resources\s+(\d+)\s+\d+\s+R/.exec(pageObject);
        const resources = elsewhere
            ? readPdfObject(pdf, elsewhere[1])
            : pageObject;
        const drawn = /\/XObject\s*<<([^>]*)>>/.exec(resources);
        if (!drawn) return [];
        const sizes: IPixelSize[] = [];
        for (const number of readPdfReferences(drawn[1])) {
            // An image is a stream, and a PDF never compresses the dictionary that describes a
            // stream, so its size can be read from the text of the file.
            const dictionary = readPdfObject(pdf, number);
            if (!/\/Subtype\s*\/Image(?![a-zA-Z])/.test(dictionary)) continue;
            const width = /\/Width\s+(\d+)/.exec(dictionary);
            const height = /\/Height\s+(\d+)/.exec(dictionary);
            if (!width || !height)
                throw new Error(
                    `A picture in the previewed PDF does not give its size: ${dictionary}`,
                );
            sizes.push({ width: Number(width[1]), height: Number(height[1]) });
        }
        return sizes;
    });
}

/** A rectangle on a PDF page, in millimetres from the left and bottom edges of the paper. */
export interface IPdfBox {
    left: number;
    bottom: number;
    right: number;
    top: number;
}

/** The boxes a PDF records for one of its pages. */
export interface IPdfPageBoxes {
    /** The whole sheet (the MediaBox): what a PDF reader reports as the paper size. */
    paper: IPdfBox;
    /**
     * Where the printer cuts the sheet (the TrimBox), which a PDF reader can draw as trim lines.
     * Undefined when the PDF records none for the page, as when it is made without print bleed.
     */
    trim?: IPdfBox;
    /**
     * How far the printing runs past the cut (the BleedBox), which a PDF reader can draw as bleed
     * lines. Undefined when the PDF records none for the page.
     */
    bleed?: IPdfBox;
}

const kMillimetresPerPoint = 25.4 / 72;

/**
 * The paper size, the trim box and the bleed box of each page of the PDF that the PDF & Print
 * screen is previewing, read from the PDF file itself. Call it after choosePdfMode().
 *
 * These are what Acrobat shows as the page size under File > Properties, and as the trim and
 * bleed lines when "Show art, trim, & bleed boxes" is on. The pages come in the order the file
 * holds them, which need not be the order of the book's pages.
 */
export async function getPdfPreviewPageBoxes(
    page: Page,
): Promise<IPdfPageBoxes[]> {
    const pdf = await readPreviewedPdf(page);
    return getPdfPageObjects(pdf).map((pageObject) => {
        const paper = readPaperSize(pdf, pageObject);
        if (!paper)
            throw new Error(
                `A page of the previewed PDF has no paper size (MediaBox): ${pageObject}`,
            );
        // A page has a trim or bleed box only when its own object gives one: unlike the paper
        // size, a PDF reader does not take these from the page tree.
        return {
            paper,
            trim: readPdfBox(pageObject, "TrimBox"),
            bleed: readPdfBox(pageObject, "BleedBox"),
        };
    });
}

/**
 * The paper size (MediaBox) of a PDF page, in millimetres. A page that does not give it itself
 * takes it from the node of the page tree it belongs to, as the PDF format says.
 */
function readPaperSize(pdf: string, pdfObject: string): IPdfBox | undefined {
    const own = readPdfBox(pdfObject, "MediaBox");
    if (own) return own;
    const parent = /\/Parent\s+(\d+)\s+(\d+)\s+R/.exec(pdfObject);
    if (!parent) return undefined;
    const parentStart = new RegExp(
        `(?:^|[\\r\\n])${parent[1]}\\s+${parent[2]}\\s+obj`,
    ).exec(pdf);
    if (!parentStart) return undefined;
    const parentEnd = pdf.indexOf("endobj", parentStart.index);
    return readPaperSize(pdf, pdf.slice(parentStart.index, parentEnd));
}

/**
 * One box that a PDF object gives, e.g. "TrimBox", in millimetres; undefined when the object does
 * not give that box.
 */
function readPdfBox(pdfObject: string, boxName: string): IPdfBox | undefined {
    const box = new RegExp(`/${boxName}\\s*\\[([^\\]]*)\\]`).exec(pdfObject);
    if (!box) return undefined;
    const numbers = box[1].trim().split(/\s+/).map(Number);
    if (numbers.length !== 4 || numbers.some((n) => Number.isNaN(n)))
        throw new Error(
            `The previewed PDF has a ${boxName} that is not four numbers: "${box[0]}".`,
        );
    const [x1, y1, x2, y2] = numbers.map((n) => n * kMillimetresPerPoint);
    // The format allows any two opposite corners, in either order.
    return {
        left: Math.min(x1, x2),
        bottom: Math.min(y1, y2),
        right: Math.max(x1, x2),
        top: Math.max(y1, y2),
    };
}

/**
 * Click the PDF & Print screen's Print button, which opens the browser's print dialog. Returns
 * when that dialog is showing. The click does not wait for the page to settle, because the print
 * dialog holds the page while it is open.
 */
export async function clickPrint(page: Page, bloomPid: number): Promise<void> {
    await page
        .getByRole("button", { name: "Print..." })
        .click({ noWaitAfter: true });
    await expect
        .poll(() => isPrintDialogOpen(bloomPid), {
            // The browser makes a preview of every page before the dialog shows its buttons.
            timeout: 60000,
            message: "The print dialog never opened after Print was clicked.",
        })
        .toBe(true);
}

/** True while the browser's print dialog is open in this Bloom. Read over UI Automation. */
export function isPrintDialogOpen(bloomPid: number): boolean {
    return runPrintDialogUia(bloomPid, "find") === "found";
}

/**
 * Close the browser's print dialog with its Cancel button, as a person would, and return when it
 * has closed.
 */
export async function cancelPrintDialog(bloomPid: number): Promise<void> {
    expect(
        runPrintDialogUia(bloomPid, "cancel"),
        "There was no print dialog to cancel.",
    ).toBe("found");
    await expect
        .poll(() => isPrintDialogOpen(bloomPid), {
            message:
                "The print dialog was still open after its Cancel button was pressed.",
        })
        .toBe(false);
}

/**
 * Find the print dialog's Cancel button in Bloom's window over UI Automation, and optionally press
 * it (InvokePattern: no pointer or keyboard) and wait for the dialog to close. Prints "found" or
 * "missing".
 */
function runPrintDialogUia(
    bloomPid: number,
    action: "find" | "cancel",
): string {
    // The print preview names its Cancel button with the class "cancel", which sets it apart from
    // any Cancel button a Bloom dialog might show at the same time.
    const script = `
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
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
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, ${bloomPid})
$byClass = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, 'cancel')
foreach ($window in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)) {
  $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byClass)
  if ($button) {
    if ('${action}' -eq 'cancel') {
      # Pressing Cancel over UI Automation lets Bloom take the foreground when the dialog
      # closes, and then whatever the developer types goes to Bloom (a space presses Print
      # again). So hand the foreground straight back to whoever had it, until the dialog is gone.
      $before = [Fg]::GetForegroundWindow()
      $beforeWasBloom = (ForegroundPid) -eq ${bloomPid}
      $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      function GiveBack { if (-not $beforeWasBloom -and (ForegroundPid) -eq ${bloomPid}) { [Fg]::SetForegroundWindow($before) | Out-Null } }
      # Bounded, so a dialog that never closes fails cancelPrintDialog's own check instead.
      $watch = [Diagnostics.Stopwatch]::StartNew()
      do { GiveBack } while ($watch.ElapsedMilliseconds -lt 30000 -and $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byClass))
      GiveBack
    }
    'found'; exit
  }
}
'missing'`;
    return execFileSync(
        "powershell",
        ["-NoProfile", "-NonInteractive", "-Command", script],
        { encoding: "utf8" },
    ).trim();
}

/**
 * The page of the print instructions window, or undefined when it is not open.
 */
async function findPrintInstructionsPage(
    browser: Browser,
): Promise<Page | undefined> {
    for (const candidate of browser.contexts().flatMap((c) => c.pages())) {
        // A page can close while we are asking; that one is not the window we want.
        const title = await candidate.title().catch(() => "");
        if (title.includes(kPrintInstructionsBundle)) return candidate;
    }
    return undefined;
}

/**
 * Wait for the print instructions window and return what it says: its heading, then each line
 * of settings, then the note, if any.
 */
export async function getPrintInstructions(page: Page): Promise<string[]> {
    const browser = page.context().browser()!;
    let instructions: Page | undefined;
    await expect
        .poll(
            async () => {
                instructions = await findPrintInstructionsPage(browser);
                return instructions !== undefined;
            },
            { message: "The print instructions window never opened." },
        )
        .toBe(true);
    const content = instructions!.locator(".print-instructions");
    await content.waitFor({ state: "visible", timeout: 30000 });
    return (await content.innerText())
        .split("\n")
        .map((line) => line.trim())
        .filter((line) => line !== "");
}

/**
 * Wait until the print instructions window shows all of its text, with nothing scrolled out of
 * sight. It waits because the window's page can still be laying its text out.
 */
export async function expectAllPrintInstructionsShowing(
    page: Page,
): Promise<void> {
    const instructions = await findPrintInstructionsPage(
        page.context().browser()!,
    );
    expect(
        instructions,
        "The print instructions window is not open.",
    ).toBeDefined();
    const window = instructions!.locator(".print-instructions");
    await expect
        .poll(
            () =>
                window.evaluate(
                    (element) => element.scrollHeight - element.clientHeight,
                ),
            {
                message:
                    "The print instructions window is too small for its text, so this many pixels of it are scrolled out of sight.",
            },
        )
        .toBeLessThanOrEqual(0);
}

/** True when the print instructions window is open. Reads the state once, without waiting. */
export async function isPrintInstructionsWindowOpen(
    page: Page,
): Promise<boolean> {
    return (
        (await findPrintInstructionsPage(page.context().browser()!)) !==
        undefined
    );
}
