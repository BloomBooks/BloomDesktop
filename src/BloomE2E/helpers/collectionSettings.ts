// Change the settings of the collection Bloom has open: its languages, its front/back matter
// pack, its branding.
//
// A person changes these in the collection Settings dialog. That dialog is a WinForms surface CDP
// cannot reach, and the settings have no API while it is closed (see AUTOMATION-DEBT.md, "WinForms
// surfaces are invisible to CDP"), so every helper here takes the route that IS open to a test:
// rewrite the .bloomCollection while Bloom is stopped, or use an E2eTestingApi hook. None of this
// is the behavior any test measures; a test that wanted to measure the Settings dialog itself could
// not be written today.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
import { makeCollectionXml } from "../fixtures/launchBloom";
import { apiPost } from "./api";

/** The collection settings a test can rewrite. Everything else keeps Bloom's defaults. */
export interface ICollectionSettings {
    /** Language tags for Language1, Language2 and Language3, in that order. */
    languages: string[];
    /**
     * The front/back matter pack's key, the part of its folder name before "-XMatter", e.g.
     * "Traditional". Left out, the collection gets the pack makeCollectionXml gives by default.
     */
    xmatterPack?: string;
}

/**
 * Give the collection these settings and start Bloom again on it, the way a person does by
 * changing them in the Settings dialog and letting Bloom restart. Returns the new shell page; the
 * old one is closed.
 *
 * The .bloomCollection is REPLACED, not edited, so every other setting goes back to what
 * makeCollectionXml writes. Use this on a collection the test itself created (collectionSpec),
 * not on a prepared collection from testing-inputs, whose other settings would be lost.
 *
 * Bloom is killed rather than asked to quit, so leave the page being edited before calling this,
 * or what was typed on it is lost (see goToPage). Each call costs about six seconds.
 */
export async function restartWithCollectionSettings(
    bloomApp: IBloomApp,
    settings: ICollectionSettings,
): Promise<Page> {
    // Read the file name rather than assuming it matches the folder name, so a collection whose
    // two names differ is rewritten instead of gaining a second .bloomCollection.
    const settingsFiles = fs
        .readdirSync(bloomApp.collectionDir)
        .filter((name) => name.endsWith(".bloomCollection"));
    if (settingsFiles.length !== 1)
        throw new Error(
            `Expected one .bloomCollection in ${bloomApp.collectionDir}, found ` +
                `${settingsFiles.length}: ${settingsFiles.join(", ")}.`,
        );
    const settingsPath = Path.join(bloomApp.collectionDir, settingsFiles[0]);
    return bloomApp.restart(() =>
        fs.writeFileSync(
            settingsPath,
            makeCollectionXml(settings.languages, settings.xmatterPack),
            "utf8",
        ),
    );
}

/**
 * Put the collection under this branding, e.g. "Story-Producer-App", and bring the selected book up
 * to date with it. This stands for entering a subscription code in the Settings dialog, which a
 * test cannot do: the dialog is WinForms, and a real code carries a checksum. The e2e/setBranding
 * hook exists for exactly this.
 *
 * The change lives in memory only. A restart puts the collection back under the branding its
 * .bloomCollection names.
 */
export async function setBranding(page: Page, branding: string): Promise<void> {
    await apiPost(page, "e2e/setBranding", branding, "text/plain");
}
