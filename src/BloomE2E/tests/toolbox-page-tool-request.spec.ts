// A page can say which tool it belongs to (data-tool-id). While the toolbox catches up, that
// request outranks whichever tool is open, so the Talking Book tool does not lay its highlight and
// audio over a page that is arriving for the Game tool (BL-14434).
//
// Releasing that request at the right moment is what this pins. Both ends of it were got wrong
// during BL-16608, and neither showed up in the unit tests, because toolbox.ts cannot be reached
// from them -- it pulls in every tool, jQuery, CKEditor's typings and the page iframe:
//
//  - Release it as soon as the page has been dealt with, and a game page reached while the toolbox
//    is SHUT loses its protection. The Game tool is offered but never opened, because opening only
//    happens while the toolbox shows, so the previously open tool -- typically Talking Book --
//    claims the page as soon as the toolbox is opened.
//  - Never release it, and the opposite happens: the user opens another tool on that page and the
//    toolbox goes on answering "game", so the tool they chose is treated as the wrong one.
//
// This asks the toolbox directly which tool it thinks the page belongs to. An earlier version
// watched for the Talking Book highlight instead, which looks like the honest user-visible
// symptom -- but that tool does not highlight a revisited game page for reasons of its own, so
// the test passed just as happily with the bug put back. Asking the toolbox is the only reading
// that tells the two apart.
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    getEffectiveToolForPage,
    hideToolbox,
    expandToolByHeader,
    showToolbox,
} from "../helpers/toolbox";

// The Games template's pages are subscription-gated; without a subscription the only game pages on
// offer come from the Activity template, and this book cannot be built at all.
test.use({
    collectionSpec: {
        name: "page-tool-request",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test("a game page reached with the toolbox shut still belongs to its own tool [Test Case ID 851]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image");
    await addPage(page, "Match Word with Image", 1, "Games");

    const pages = await getContentPages(page);
    const [textPage, gamePage] = pages.slice(-2);

    // Sanity check: an ordinary page requires nothing, so the toolbox answers with whatever tool is
    // open. If this ever reported "game", the readings below would mean nothing.
    await showToolbox(page);
    await expandToolByHeader(page, "talkingBook");
    await goToPage(page, textPage.id);
    await waitForEditablePage(page);
    expect(
        await getEffectiveToolForPage(page),
        "On a page that requires no tool, the toolbox should answer with the open tool.",
    ).toBe("talkingBook");

    // The case that regressed. Reach the game page with the toolbox shut: the Game tool is offered
    // but not opened, so Talking Book is still the open tool -- and the page must still belong to
    // the Game tool, or Talking Book will claim its audio and highlighting.
    await hideToolbox(page);
    await goToPage(page, gamePage.id);
    await waitForEditablePage(page);
    await showToolbox(page);
    expect(
        await getEffectiveToolForPage(page),
        "A game page reached with the toolbox shut was handed to whichever tool happened to be " +
            "open, instead of being kept for the Game tool.",
    ).toBe("game");

    // The opposite end. Opening another tool is the user deciding what they are doing with this
    // page, and the request must not outlive that: left latched, the toolbox would go on answering
    // "game" for a page whose open tool is now something else.
    await expandToolByHeader(page, "settings");
    await expandToolByHeader(page, "talkingBook");
    expect(
        await getEffectiveToolForPage(page),
        "The page's request outlived the user opening another tool.",
    ).toBe("talkingBook");
});
