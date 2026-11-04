// Drive the Image Chooser, the dialog that "Choose image from your computer..." opens on any
// picture (ImageGalleryDialog.tsx, which hosts the bloom-image-gallery package).
//
// Its "Open File..." button, under "This Computer", ends in a native file picker
// (imageGallery/pickLocalImageFile), which would hang a run (AUTOMATION-DEBT.md, "Native OS
// dialogs hang automation"). That picker is a BloomOpenFileDialog, so armFileChooser answers it
// instead, and everything else in the dialog is clicked the way a person clicks it. Use this when
// the test is about what happens to the picture element the dialog was opened on, which the
// dialog holds from the moment it opens until "Use this image". Tests that only need a picture
// in place take the faster route in helpers/images.ts or helpers/inlineImages.ts.
//
// The gallery package gives its buttons no test ids, so this finds them by their English labels.
// The suite runs Bloom in English (helpers/uiLanguage.ts changes that only on purpose).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { armFileChooser } from "./talkingBook";

const kOpenFileLabel = "Open File...";
const kUseThisImageLabel = "Use this image";

/**
 * The open Image Chooser in one document. The dialog is rendered by ShowEditViewDialog, which puts
 * it in the document of the page being edited, but the shell is searched too, so a change of host
 * document fails on the label rather than on a missing dialog.
 */
function chooserIn(frame: Frame | Page): Locator {
    return frame.getByRole("dialog").filter({
        has: frame.getByRole("button", { name: kOpenFileLabel }),
    });
}

/** Wait until the Image Chooser is showing, and return it, in whichever document holds it. */
async function waitForImageChooser(page: Page): Promise<Locator> {
    const candidates = [chooserIn(editablePageFrame(page)), chooserIn(page)];
    let found: Locator | undefined;
    await expect
        .poll(
            async () => {
                for (const candidate of candidates) {
                    if (await candidate.isVisible().catch(() => false)) {
                        found = candidate;
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: 30000,
                message: `The Image Chooser, with its "${kOpenFileLabel}" button, never showed.`,
            },
        )
        .toBe(true);
    return found!;
}

/**
 * In the Image Chooser that is open, choose the picture at `filePath` from this computer and click
 * "Use this image", the way a person does. Returns once the dialog has closed. What the picture
 * element then shows is the caller's question.
 */
export async function chooseFileInImageChooser(
    page: Page,
    filePath: string,
): Promise<void> {
    const chooser = await waitForImageChooser(page);
    // Armed immediately before the click that opens the picker, because it answers one picker
    // only, and a path left armed would answer some later test's picker.
    await armFileChooser(page, filePath);
    await chooser.getByRole("button", { name: kOpenFileLabel }).click();
    const use = chooser.getByRole("button", { name: kUseThisImageLabel });
    await expect(
        use,
        `After "${kOpenFileLabel}" was answered with ${filePath}, the Image Chooser never ` +
            `enabled "${kUseThisImageLabel}", so it did not take the file.`,
    ).toBeEnabled({ timeout: 30000 });
    await use.click();
    await expect(
        chooser,
        `The Image Chooser stayed open after "${kUseThisImageLabel}". It stays open when Bloom ` +
            `could not add the picture, and shows a message saying so.`,
    ).toHaveCount(0, { timeout: 30000 });
}
