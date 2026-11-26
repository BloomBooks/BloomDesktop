// Text in Language 2 on a Digital Comic Book's End Extra 1, 2 and 3 pages reaches the BloomPUB,
// whether or not Language 2 is ticked in the Publish tab's Text Languages list. Automates the
// manual test "Digital Comic Cover Content" (Test Case ID 198), which repeats "Cover Content In
// BloomPUB" (Test Case ID 197, bloompub-cover-content.spec.ts) with a Digital Comic Book.
//
// A Digital Comic Book requires the Device front/back matter whatever the collection's pack, so in
// the Edit tab it already has the inside front, inside back and outside back covers at the back,
// named End Extra 1, 2 and 3. helpers/bloomPubCoverContent.ts says why they could go missing from
// a BloomPUB.

import { expect, test } from "../fixtures/bloomTest";
import { getPages, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    addBilingualContentPage,
    expectBlankTextOnlyPageLeftOut,
    expectCoverPagesInBloomPub,
    type ICoverContentCase,
    openBloomPubScreenWithOptionalL2,
    setL2Published,
    typeLanguage2OnCoverPages,
} from "../helpers/bloomPubCoverContent";

const LANGUAGES = ["fr", "de"];
const [L1, L2] = LANGUAGES;

test.use({
    collectionSpec: {
        name: "bloompub-comic-cover-content",
        languages: LANGUAGES,
    },
});

const COVER_CASE: ICoverContentCase = {
    l1: L1,
    l2: L2,
    coverPages: [
        {
            caption: "End Extra 1",
            xmatterPage: "insideFrontCover",
            text: "Innenseite vorne",
        },
        {
            caption: "End Extra 2",
            xmatterPage: "insideBackCover",
            text: "Innenseite hinten",
        },
        {
            caption: "End Extra 3",
            xmatterPage: "outsideBackCover",
            text: "Rückseite",
        },
    ],
    textOnlyPage: "insideFrontCover",
};

test("Language 2 text on a Digital Comic Book's End Extra pages is in the BloomPUB whether or not Language 2 is published [Test Case ID 198]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    await step(
        "Make a Digital Comic Book with a content page in both languages",
        async () => {
            await makeBookFromTemplate(page, "Digital Comic Book");
            // The End Extra pages are the Device front/back matter's; the collection's own pack
            // has none, so seeing them shows that the book took the Device pack.
            const captions = (await getPages(page)).map((p) => p.caption);
            for (const coverPage of COVER_CASE.coverPages)
                expect(
                    captions,
                    `test setup: a Digital Comic Book should have an "${coverPage.caption}" page`,
                ).toContain(coverPage.caption);
            await addBilingualContentPage(page, COVER_CASE);
        },
    );

    await step(
        "While End Extra 1 is blank, the BloomPUB leaves it out",
        async () => expectBlankTextOnlyPageLeftOut(page, COVER_CASE),
    );

    await step("Type Language 2 text on End Extra 1, 2 and 3", async () =>
        typeLanguage2OnCoverPages(page, COVER_CASE),
    );

    const l2Name = await step("Open the BloomPUB screen", async () =>
        openBloomPubScreenWithOptionalL2(page, COVER_CASE),
    );
    if (l2Name === undefined) throw new Error("No Language 2 name was read.");

    await step("Tick Language 2 and preview the BloomPUB", async () => {
        await setL2Published(page, COVER_CASE, l2Name, true);
        await expectCoverPagesInBloomPub(page, COVER_CASE, true);
    });

    await step("Untick Language 2 and preview the BloomPUB again", async () => {
        await setL2Published(page, COVER_CASE, l2Name, false);
        await expectCoverPagesInBloomPub(page, COVER_CASE, false);
    });
});
