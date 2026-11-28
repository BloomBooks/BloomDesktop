// Journey test for Bloom's help buttons: each one opens Bloom's help file at a topic about the place
// it was pressed in. A person presses Help... in the help menu on each of the three workspace tabs,
// the Help link of each toolbox tool that has one, and the Help button on each tab of the two
// reader setup dialogs. Bloom opens the help file in the Windows HTML Help viewer, and the test
// reads the topic that viewer shows: its file must be Bloom.chm, the page must have loaded from it
// (a topic missing from the help file shows the viewer's own error page instead), and its title
// must name what the button is about.

import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { openHelpAndReadTopic } from "../helpers/helpWindow";
import { chooseImageFile } from "../helpers/images";
import {
    cancelReaderSetup,
    clickDecodableSetupHelp,
    clickLeveledSetupHelp,
    closeLeveledReaderSetup,
    enableDecodableReaderTool,
    enableLeveledReaderTool,
    openDecodableStagesSetup,
    openLeveledReaderSetup,
    openLeveledReaderSetupTab,
    openReaderSetupTab,
} from "../helpers/readerSetup";
import {
    clickToolHelpLink,
    enableToolForBook,
    type ToolId,
} from "../helpers/toolbox";
import { chooseHelpFromHelpMenu, switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "help-buttons",
        languages: ["en"],
        // Some tools (the Canvas Tool, the Motion Tool) need a subscription. Below their tier the
        // tool's panel lies under a notice that also covers its Help link (BL-17220).
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// Each toolbox tool whose panel has a Help link, and what the title of the topic it opens names.
const kToolTopics: [ToolId, RegExp][] = [
    ["canvas", /Canvas Tool/i],
    ["imageDescription", /Image Description/i],
    ["impairmentVisualizer", /Impairment Visualizer/i],
    ["motion", /Motion Tool/i],
    ["signLanguage", /Sign Language/i],
    ["talkingBook", /Talking Book/i],
];

test("Help buttons open a topic of Bloom's help file about where they were pressed [Test Case ID 653]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.skip(
        launchWithDontDisturb(),
        "Bloom's help window takes the foreground, so this runs only where Bloom may take it: on " +
            "CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );
    const pid = bloomApp.bloomPid;

    /** Do `press`, and check that the help topic it opens has a title that `about` matches. */
    const expectHelpAbout = async (
        what: string,
        about: RegExp,
        press: () => Promise<void>,
    ) => {
        const shown = await openHelpAndReadTopic(pid, press, what);
        expect(shown.helpFile, `the help file that ${what} opened`).toBe(
            "Bloom.chm",
        );
        expect(
            shown.title,
            `the title of the topic that ${what} opened (${shown.topic})`,
        ).toMatch(about);
    };

    await step("Collection tab", async () => {
        await expectHelpAbout(
            "Help... on the Collections tab",
            /Collections tab/i,
            () => chooseHelpFromHelpMenu(page),
        );
    });

    const bookFolder = await makeBookFromTemplate(page, "Basic Book");

    await step("Edit tab", async () => {
        await expectHelpAbout("Help... on the Edit tab", /Edit tab/i, () =>
            chooseHelpFromHelpMenu(page),
        );
    });

    await step("Publish tab", async () => {
        await switchTab(page, "publish");
        await expectHelpAbout("Help... on the Publish tab", /Publish/i, () =>
            chooseHelpFromHelpMenu(page),
        );
        await switchTab(page, "edit");
    });

    await step("Toolbox tools", async () => {
        for (const [tool] of kToolTopics)
            await enableToolForBook(page, bookFolder, tool);
        // A content page with a picture, where every one of these tools can work: on a page
        // where the Motion Tool cannot (the cover, a page with no picture) its Help link cannot
        // be clicked (BL-17220).
        await addPage(page, "Basic Text & Image");
        await chooseImageFile(page, kPicture);
        for (const [tool, about] of kToolTopics)
            await expectHelpAbout(`the ${tool} tool's Help link`, about, () =>
                clickToolHelpLink(page, tool),
            );
    });

    await step("Decodable Reader setup dialog tabs", async () => {
        await enableDecodableReaderTool(page);
        await openDecodableStagesSetup(page);
        const tabs: ["letters" | "sampleWords" | "stages", RegExp][] = [
            ["letters", /Letters/i],
            ["sampleWords", /Words/i],
            ["stages", /Stages/i],
        ];
        for (const [tab, about] of tabs) {
            await openReaderSetupTab(page, tab);
            await expectHelpAbout(
                `Help on the Decodable Reader setup's ${tab} tab`,
                about,
                () => clickDecodableSetupHelp(page),
            );
        }
        await cancelReaderSetup(page);
    });

    await step("Leveled Reader setup dialog tabs", async () => {
        await enableLeveledReaderTool(page);
        await openLeveledReaderSetup(page);
        const tabs: ["punctuation" | "readerLevels", RegExp][] = [
            ["punctuation", /Punctuation/i],
            ["readerLevels", /Reader Levels/i],
        ];
        for (const [tab, about] of tabs) {
            await openLeveledReaderSetupTab(page, tab);
            await expectHelpAbout(
                `Help on the Leveled Reader setup's ${tab} tab`,
                about,
                () => clickLeveledSetupHelp(page),
            );
        }
        await closeLeveledReaderSetup(page);
    });
});
