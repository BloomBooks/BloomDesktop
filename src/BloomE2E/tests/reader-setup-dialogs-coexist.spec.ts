// Only the Decodable Reader's setup dialog was converted to React. The Leveled Reader's is still
// the old jQuery one, and the two now share the code that closes a reader setup dialog:
// closeSetupDialog decides between them by asking whether a jQuery dialog is present, and routes
// to the React one when it is not.
//
// That branch is the seam between the converted and unconverted halves of this feature, and a
// fault in it would show as the wrong dialog closing, or one refusing to close at all. Nothing
// else tests it, and it is the spot a code comment already warns whoever converts the Leveled
// Reader next. (BL-16607)

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { getShownTools } from "../helpers/toolbox";
import {
    cancelReaderSetup,
    closeLeveledReaderSetup,
    enableDecodableReaderTool,
    enableLeveledReaderTool,
    isDecodableSetupDialogShowing,
    isLeveledSetupDialogShowing,
    openDecodableStagesSetup,
    openLeveledReaderSetup,
} from "../helpers/readerSetup";

test.use({ collectionSpec: { name: "reader-dialogs", languages: ["en"] } });

test("the legacy Levels dialog and the React Stages dialog each open and close on their own", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
    await enableLeveledReaderTool(page);

    // The converted one first.
    await openDecodableStagesSetup(page);
    expect(await isDecodableSetupDialogShowing(page)).toBe(true);
    expect(
        await isLeveledSetupDialogShowing(page),
        "Opening the React Stages dialog should not bring up the legacy one.",
    ).toBe(false);

    await cancelReaderSetup(page);
    expect(await isDecodableSetupDialogShowing(page)).toBe(false);

    // Then the one still on the old code path, in the same session.
    await openLeveledReaderSetup(page);
    expect(await isLeveledSetupDialogShowing(page)).toBe(true);
    expect(
        await isDecodableSetupDialogShowing(page),
        "Opening the legacy Levels dialog should not bring up the React one.",
    ).toBe(false);

    await closeLeveledReaderSetup(page);
    expect(
        await isLeveledSetupDialogShowing(page),
        "The legacy Levels dialog would not close.",
    ).toBe(false);

    // And back to the React one afterwards, to catch a close that left shared state wrong.
    await openDecodableStagesSetup(page);
    expect(await isDecodableSetupDialogShowing(page)).toBe(true);
    await cancelReaderSetup(page);
    expect(await isDecodableSetupDialogShowing(page)).toBe(false);

    // sanity check: the toolbox is still usable after all that
    expect(
        await getShownTools(page),
        "The toolbox stopped offering the Decodable Reader tool.",
    ).toContain("decodableReader");
});
