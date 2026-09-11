// The Sample Words tab shows a "Help exporting and converting files" link beside any file in the
// Sample Texts folder that Bloom cannot read. Two things about that link have been wrong, and
// both are the kind only a running Bloom catches:
//
// 1. It was a plain anchor to the help api. In the legacy dialog that was harmless, because that
//    dialog lived in a throw-away iframe; this one is rendered in the workspace document, so
//    following the link navigated the whole edit view away from the book the user was editing.
// 2. Its topic, carried over from the legacy markup, was a page that does not exist in Bloom.chm
//    -- so once the navigation was fixed, help opened with no topic at all.
//
// This test pins both: the topic it asks for, and that the book is still on screen afterwards.
// It answers the help request itself rather than letting it reach Bloom, because a real one opens
// the Windows help viewer -- a native window an automated run must not leave behind. (BL-16607)

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { editablePageFrame, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    clickSampleTextsHelpLink,
    enableDecodableReaderTool,
    openDecodableStagesSetup,
    openReaderSetupTab,
} from "../helpers/readerSetup";

// The Sample Words tab's own help page, and one that really is in Bloom.chm.
const EXPECTED_TOPIC =
    "topic=Tasks/Edit_tasks/Decodable_Reader_Tool/Words_tab.htm";

test.use({ collectionSpec: { name: "reader-help-link", languages: ["en"] } });

test("the sample-texts help link asks for a real topic and leaves the book showing", async ({
    page,
    bloomApp,
}) => {
    await makeBookFromTemplate(page, "Basic Book");

    // The link appears only when something in Sample Texts cannot be read.
    const sampleTexts = Path.join(bloomApp.collectionDir, "Sample Texts");
    fs.mkdirSync(sampleTexts, { recursive: true });
    fs.writeFileSync(Path.join(sampleTexts, "notes.docx"), "not really a docx");
    fs.writeFileSync(Path.join(sampleTexts, "readable.txt"), "cat sat mat");

    await enableDecodableReaderTool(page);
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "sampleWords");

    const requestedUrl = await clickSampleTextsHelpLink(page);

    expect(requestedUrl).toContain(EXPECTED_TOPIC);
    // The page this link used to name does not exist under Decodable_Reader_Tool, which is what
    // made help open blank.
    expect(requestedUrl).not.toContain("Language_tab.htm");

    // sanity check: the edit view is still showing the book, rather than having followed the link
    expect(
        await editablePageFrame(page).locator(".bloom-page").count(),
        "Clicking the help link navigated the edit view away from the book.",
    ).toBeGreaterThan(0);
});
