// The Game row of the Features list on Publish: Web. Its check mark shows exactly when the book has
// a game page that the collection's subscription lets it publish. Below the Pro tier that games
// need, the row is off and its tooltip names the tier; with no game page left in the book, it is
// off and says the book has no games.
//
// Test Case ID 174 ("Game Feature Enterprise Rules"). The tier comes from the subscription code
// in the .bloomCollection, which Bloom reads only as it opens the collection, so each change of
// tier is a restart (restartWithCollectionSettings), and the page object from before it is dead
// afterwards.

import { expect, test } from "../fixtures/bloomTest";
import { apiGetJson } from "../helpers/api";
import { getContentPages, goToPage } from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    getFeatureStatus,
    kEnterpriseSubscriptionCode,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import { makeBookWithGamePage } from "../helpers/games";
import { openPublishToWeb } from "../helpers/libraryPublish";
import {
    confirmRemovePageDialog,
    openRemovePageDialog,
} from "../helpers/pageList";
import { expectGameFeature, getGameTooltipText } from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "publish-game-feature",
        languages: ["en"],
        // Games need a subscription that includes them, and the book has to be built first.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test("the Game feature follows the book's game pages and the subscription tier [Test Case ID 174]", async ({
    page,
    step,
    bloomApp,
}) => {
    // Building the book and two restarts.
    test.setTimeout(600000);

    const book = await step("Make a book with a game page", async () =>
        makeBookWithGamePage(page, "Drag Images to Targets"),
    );
    if (!book) throw new Error("The book with a game page was never made.");

    await step("With Enterprise, see the Game feature on", async () => {
        await openPublishToWeb(page);
        await expectGameFeature(
            page,
            {
                checked: true,
                disabled: false,
                tooltip: await getGameTooltipText(page, "hasGames"),
            },
            "With Enterprise and a game page, the Game feature should be on.",
        );
    });

    const basic = await step("Turn Enterprise off", async () => {
        await switchTab(page, "collection");
        return restartWithCollectionSettings(bloomApp, { languages: ["en"] });
    });
    if (!basic) throw new Error("Bloom never restarted without Enterprise.");

    await step("See the Game feature off, needing Pro", async () => {
        const game = await getFeatureStatus(basic, "game");
        expect(
            { enabled: game.enabled, subscriptionTier: game.subscriptionTier },
            `Sanity check: with no subscription code, games should be off the tier, and need ` +
                `Pro. Bloom's whole answer: ${JSON.stringify(game)}`,
        ).toEqual({ enabled: false, subscriptionTier: "Pro" });
        await selectBook(basic, book.bookFolder);
        await openPublishToWeb(basic);
        await expectGameFeature(
            basic,
            {
                checked: false,
                disabled: true,
                tooltip: await getGameTooltipText(basic, "needsHigherTier"),
            },
            "Below Pro, the Game feature should be off and say it needs Pro.",
        );
    });

    const enterprise = await step("Turn Enterprise on again", async () => {
        await switchTab(basic, "collection");
        return restartWithCollectionSettings(bloomApp, {
            languages: ["en"],
            subscriptionCode: kEnterpriseSubscriptionCode,
        });
    });
    if (!enterprise)
        throw new Error("Bloom never restarted with Enterprise again.");

    await step("See the Game feature on again", async () => {
        await selectBook(enterprise, book.bookFolder);
        await openPublishToWeb(enterprise);
        await expectGameFeature(
            enterprise,
            {
                checked: true,
                disabled: false,
                tooltip: await getGameTooltipText(enterprise, "hasGames"),
            },
            "With Enterprise again, the Game feature should be on again.",
        );
    });

    await step("Remove the game page in Edit", async () => {
        await switchTab(enterprise, "edit");
        await goToPage(enterprise, book.gamePageId);
        await openRemovePageDialog(enterprise);
        await confirmRemovePageDialog(enterprise);
        expect(
            (await getContentPages(enterprise)).map((p) => p.id),
            "The game page should be gone from the book.",
        ).not.toContain(book.gamePageId);
    });

    await step("See the Game feature off, with no games", async () => {
        await openPublishToWeb(enterprise);
        // The row the screen shows before its requests answer is this same "no games" row, so
        // the row alone could be read too early. Bloom's own answers settle it: with no game page
        // and no widget, that row is the one the screen ends up showing.
        expect(
            {
                nonWidgetGames: await apiGetJson<boolean>(
                    enterprise,
                    "publish/hasNonWidgetGames",
                ),
                widgets: await apiGetJson<boolean>(
                    enterprise,
                    "publish/hasWidgets",
                ),
            },
            "Bloom should say the book has no games left.",
        ).toEqual({ nonWidgetGames: false, widgets: false });
        await expectGameFeature(
            enterprise,
            {
                checked: false,
                disabled: true,
                tooltip: await getGameTooltipText(enterprise, "noGames"),
            },
            "With no game page left, the Game feature should be off and say there are no games.",
        );
    });
});
