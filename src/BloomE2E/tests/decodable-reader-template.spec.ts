// A book made from the Decodable Reader template opens with the Decodable Reader tool showing, on
// the first stage.
//
// This pins the toolbox deciding, from the book alone, which tool to offer and open: the template
// turns the tool on and makes it the current one, with no click from the person. The toolbox
// infrastructure rewrite (BL-16608) rewrote that decision. It is in a file of its own because the
// stage a new book starts on is the one last chosen with the stage arrows (see
// reader-tool-stage-and-level.spec.ts), and that is remembered for the whole run of Bloom; only a
// Bloom that has never had a stage chosen shows what a book starts on by default.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { expectReaderToolToShow, openReaderTool } from "../helpers/readerTools";
import { expectOpenTool, getShownTools, showToolbox } from "../helpers/toolbox";

test.use({
    collectionSpec: { name: "decodable-reader-template", languages: ["en"] },
});

test("a book made from the Decodable Reader template opens on that tool, on stage 1 [Test Case ID 436]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Decodable Reader");
    await showToolbox(page);

    expect(
        await getShownTools(page),
        "The toolbox should offer the Decodable Reader tool without anyone turning it on.",
    ).toContain("decodableReader");
    await expectOpenTool(
        page,
        "decodableReader",
        "The Decodable Reader tool should be the one open in a new decodable book.",
    );

    await openReaderTool(page, "decodableReader");
    await expectReaderToolToShow(
        page,
        "decodableReader",
        1,
        "A decodable book made in a Bloom where no stage has been chosen should start on stage 1.",
    );
});
