// Journey test for the Help button of the new (React) Collection Settings dialog: on each page, a
// person clicks Help and Bloom is asked for that page's help topic. The old dialog's topics carry
// over to the pages that took over its tabs; the rest get the general Settings topic.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    clickCollectionSettingsHelp,
    openCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";

const kGeneralTopic = "User_Interface/Dialog_boxes/Settings_dialog_box.htm";

// Each page, by the name the dialog shows, and the topic its Help asks for.
const kTopicByPage: [string, string][] = [
    ["Languages", "Tasks/Basic_tasks/Change_languages.htm"],
    [
        "Front & Back Matter",
        "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm",
    ],
    ["Subscription", "Tasks/Basic_tasks/Enter_Subscription_Code.htm"],
    ["Team Collection", kGeneralTopic],
    ["Bloom Library", kGeneralTopic],
    ["Advanced", kGeneralTopic],
    ["Experimental", kGeneralTopic],
];

test.use({
    collectionSpec: { name: "settings-help", languages: ["en"] },
    // The Team Collection page shows only while Team Collections is turned on.
    experimentalFeatures: ["team-collections"],
});

test("Help in the Collection Settings dialog opens the topic for the page showing [Test Case ID 850]", async ({
    page,
}) => {
    await openCollectionSettings(page);
    // The dialog opens on its first page, with nothing clicked yet.
    expect(await clickCollectionSettingsHelp(page)).toBe(kTopicByPage[0][1]);

    for (const [pageName, topic] of kTopicByPage) {
        await showCollectionSettingsPage(page, pageName);
        expect(
            await clickCollectionSettingsHelp(page),
            `Help on the ${pageName} page`,
        ).toBe(topic);
    }
    await cancelCollectionSettings(page);
});
