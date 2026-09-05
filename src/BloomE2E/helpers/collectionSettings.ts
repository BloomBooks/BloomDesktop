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
import {
    makeCollectionXml,
    type IRelaunchChanges,
} from "../fixtures/launchBloom";
import { apiGetJson, apiPost } from "./api";

/** The collection settings a test can rewrite. Everything else keeps Bloom's defaults. */
export interface ICollectionSettings {
    /** Language tags for Language1, Language2 and Language3, in that order. */
    languages: string[];
    /**
     * The front/back matter pack's key, the part of its folder name before "-XMatter", e.g.
     * "Traditional". Left out, the collection gets the pack makeCollectionXml gives by default.
     */
    xmatterPack?: string;
    /**
     * A subscription code, which decides the collection's tier; see kProSubscriptionCode. Left
     * out, the collection has no code and so is Basic.
     */
    subscriptionCode?: string;
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
 *
 * `changes` goes on to bloomApp.restart, so the same restart can also change what only a launch
 * can change, such as which experimental features are on. A test whose subject is a feature gated
 * both by a tier and by an experiment needs both in one step.
 */
export async function restartWithCollectionSettings(
    bloomApp: IBloomApp,
    settings: ICollectionSettings,
    changes?: IRelaunchChanges,
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
    return bloomApp.restart(
        () =>
            fs.writeFileSync(
                settingsPath,
                makeCollectionXml(
                    settings.languages,
                    settings.xmatterPack,
                    settings.subscriptionCode,
                ),
                "utf8",
            ),
        changes,
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

/** Bloom's subscription tiers, lowest first. A tier includes every lower tier's features. */
export type SubscriptionTier =
    | "Basic"
    | "Pro"
    | "LocalCommunity"
    | "Enterprise";

/**
 * A subscription code that puts a collection on the Pro tier. Give it to a test's collectionSpec
 * (or to restartWithCollectionSettings) when the test's subject is a Pro feature, such as tables.
 *
 * It has to be a real code rather than an API hook that sets the tier: Bloom reads the tier out of
 * the code as it opens the collection, and several parts of Bloom then keep the Subscription object
 * they were handed at startup, so a tier changed later is invisible to them. FeatureStatusApi is
 * one, which means the Canvas tool's palette goes on hiding the table item however the tier is
 * changed after launch.
 *
 * The code is made the way Bloom's own code-generating sheet does it: a descriptor whose last part
 * is "Pro", the expiry date as days since 1899-12-30 less 40000, and a checksum of
 * (floor(sqrt(datePart)) + sum of descriptor[i] * i, uppercased) modulo 10000. See
 * Subscription.CalculateTier and Subscription.IsChecksumCorrect. This one expires on 1 January
 * 2040, after which Bloom will call the collection Basic and every Pro test will fail; make a new
 * one the same way. The descriptor names no branding Bloom ships, and an unknown branding falls
 * back to Default silently (BookStorage.UpdateSupportFiles), so the collection looks ordinary.
 */
export const kProSubscriptionCode = "Bloom-E2E-Pro-011136-5480";

/**
 * What Bloom says about one feature, as features/status reports it. It answers with more than
 * this, all of it about how to word the message offering an upgrade; these are the fields that say
 * whether the feature works.
 */
export interface IFeatureStatus {
    /**
     * The tier the feature REQUIRES, not the tier the collection has. So this says "Pro" for
     * tables whatever the collection's own subscription is; `enabled` is what tells you whether
     * the collection reaches it.
     */
    subscriptionTier: SubscriptionTier;
    /** True when the collection's tier reaches the feature's. */
    enabled: boolean;
    /** True when Bloom should show the feature's controls at all: for tables, the experiment. */
    visible: boolean;
}

/**
 * Ask Bloom whether a feature is available here, e.g. getFeatureStatus(page, "table").
 *
 * This is the same answer the front end asks for before it decides whether to show a feature's
 * controls, so it is the right sanity check for a test whose subject is behind a subscription tier
 * or an experiment: when the control is missing, this says whether the tier or the experiment is
 * the reason.
 */
export async function getFeatureStatus(
    page: Page,
    featureName: string,
): Promise<IFeatureStatus> {
    return apiGetJson<IFeatureStatus>(
        page,
        `features/status?featureName=${encodeURIComponent(featureName)}&forPublishing=false`,
    );
}
