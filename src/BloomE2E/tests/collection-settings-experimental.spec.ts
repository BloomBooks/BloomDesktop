// Journey test for the Experimental page of the new (React) Collection Settings dialog: a person
// opens Settings, turns on Team Collections, and presses Restart; Bloom saves the choice and
// reopens the collection.

import { expect, test } from "../fixtures/bloomTest";
import {
    getCollectionSettingsCheckbox,
    getCollectionSettingsOkLabel,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
    toggleCollectionSettingsCheckbox,
} from "../helpers/collectionSettings";
import { getSavedExperimentalFeatures } from "../helpers/userSettings";

// Team Collections needs a subscription tier above Basic, or its checkbox is disabled.
test.use({
    collectionSpec: {
        name: "experimental-settings",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kTeamCollectionsToken = "team-collections";

test("turning on Team Collections on the Experimental page saves it and reopens the collection [Test Case ID 837]", async ({
    page,
    bloomApp,
}) => {
    // The launch gives this Bloom an empty settings folder, so nothing is turned on yet.
    expect(
        await getSavedExperimentalFeatures(bloomApp.userSettingsDir),
    ).not.toContain(kTeamCollectionsToken);

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Experimental");
    // Polled: the box stays disabled until Bloom has said the subscription includes the feature.
    await expect
        .poll(() => getCollectionSettingsCheckbox(page, "Team Collections"), {
            message: "Team Collections should be offered, unticked",
        })
        .toEqual({ checked: false, enabled: true });
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    await toggleCollectionSettingsCheckbox(page, "Team Collections");

    // Turning the feature on or off needs a restart, so OK says so.
    await expect
        .poll(() => getCollectionSettingsOkLabel(page), {
            message: "OK should become Restart once Team Collections changes",
        })
        .toBe("Restart");

    await restartFromCollectionSettings(bloomApp);

    expect(
        await getSavedExperimentalFeatures(bloomApp.userSettingsDir),
    ).toContain(kTeamCollectionsToken);
});
