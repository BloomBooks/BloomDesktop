// Text in Language 2 on the inside front cover, the inside back cover and the outside back cover
// reaches the BloomPUB, whether or not Language 2 is ticked in the Publish tab's Text Languages
// list. Automates the manual test "Cover Content In BloomPUB" (Test Case ID 197).
//
// helpers/bloomPubCoverContent.ts says why these pages could go missing from a BloomPUB.
//
// The collection gets the Traditional pack, the one the Settings dialog calls the default, because
// it is the pack that has all three pages; the pack a test collection starts with puts the credits
// on the inside front cover instead.

import { test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    addBilingualContentPage,
    expectCoverPagesInBloomPub,
    type ICoverContentCase,
    openBloomPubScreenWithOptionalL2,
    setL2Published,
    typeLanguage2OnCoverPages,
} from "../helpers/bloomPubCoverContent";
import { restartWithCollectionSettings } from "../helpers/collectionSettings";

const LANGUAGES = ["fr", "de"];
const [L1, L2] = LANGUAGES;

test.use({
    collectionSpec: { name: "bloompub-cover-content", languages: LANGUAGES },
});

const COVER_CASE: ICoverContentCase = {
    l1: L1,
    l2: L2,
    coverPages: [
        {
            caption: "Inside Front Cover",
            xmatterPage: "insideFrontCover",
            text: "Innenseite vorne",
        },
        {
            caption: "Inside Back Cover",
            xmatterPage: "insideBackCover",
            text: "Innenseite hinten",
        },
        {
            caption: "Outside Back Cover",
            xmatterPage: "outsideBackCover",
            text: "Rückseite",
        },
    ],
    textOnlyPage: "insideFrontCover",
};

test("Language 2 text on the inside and back covers is in the BloomPUB whether or not Language 2 is published [Test Case ID 197]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(420000);

    const page = await step(
        "Give the collection the Traditional front/back matter",
        async () =>
            restartWithCollectionSettings(bloomApp, {
                languages: LANGUAGES,
                xmatterPack: "Traditional",
            }),
    );
    if (!page) throw new Error("Bloom did not come back after the restart.");

    await step(
        "Make a Basic Book with a content page in both languages",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addBilingualContentPage(page, COVER_CASE);
        },
    );

    await step(
        "Type Language 2 text on the inside front, inside back and outside back covers",
        async () => typeLanguage2OnCoverPages(page, COVER_CASE),
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
