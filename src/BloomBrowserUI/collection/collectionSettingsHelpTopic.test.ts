import { describe, expect, it } from "vitest";
import {
    collectionSettingsHelpTopic,
    kGeneralSettingsHelpTopic,
} from "./collectionSettingsHelpTopic";

describe("collectionSettingsHelpTopic", () => {
    it.each([
        ["languages", "Tasks/Basic_tasks/Change_languages.htm"],
        [
            "frontBackMatter",
            "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm",
        ],
        ["subscription", "Tasks/Basic_tasks/Enter_Subscription_Code.htm"],
        ["teamCollection", kGeneralSettingsHelpTopic],
        ["bloomLibrary", kGeneralSettingsHelpTopic],
        ["advanced", kGeneralSettingsHelpTopic],
        ["experimental", kGeneralSettingsHelpTopic],
    ])("gives the %s page the topic %s", (pageKey, topic) => {
        expect(collectionSettingsHelpTopic(pageKey)).toBe(topic);
    });

    it("gives the general Settings topic when no page is showing", () => {
        expect(kGeneralSettingsHelpTopic).toBe(
            "User_Interface/Dialog_boxes/Settings_dialog_box.htm",
        );
        expect(collectionSettingsHelpTopic(undefined)).toBe(
            kGeneralSettingsHelpTopic,
        );
    });
});
