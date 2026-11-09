// Drive and read the Publish tab's PDF & Print screen: its booklet modes, its Print button, the
// browser's print dialog that button opens, and the window of booklet print instructions Bloom
// shows beside that dialog.
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
 * it (InvokePattern: no pointer or keyboard). Prints "found" or "missing".
 */
function runPrintDialogUia(
    bloomPid: number,
    action: "find" | "cancel",
): string {
    // The print preview names its Cancel button with the class "cancel", which sets it apart from
    // any Cancel button a Bloom dialog might show at the same time.
    const script = `
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, ${bloomPid})
$byClass = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, 'cancel')
foreach ($window in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)) {
  $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byClass)
  if ($button) {
    if ('${action}' -eq 'cancel') {
      $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
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

/** True when the print instructions window is open. Reads the state once, without waiting. */
export async function isPrintInstructionsWindowOpen(
    page: Page,
): Promise<boolean> {
    return (
        (await findPrintInstructionsPage(page.context().browser()!)) !==
        undefined
    );
}

/** Wait until the print instructions window has closed. */
export async function expectPrintInstructionsWindowClosed(
    page: Page,
    message: string,
): Promise<void> {
    await expect
        .poll(() => isPrintInstructionsWindowOpen(page), { message })
        .toBe(false);
}
