// Where the Bloom under test keeps its user settings (user.config: UI language, page zoom, the
// Bloom Library login, and the rest of Settings.Default), and what it has saved there.
//
// The launch fixture gives every Bloom it starts a settings folder of its own inside the run's
// temp folder (bloomApp.userSettingsDir; see fixtures/launchBloom.ts), so a test can look at the
// settings its Bloom saved with nobody else's Bloom in the way, and can put settings there before
// a launch for Bloom to start from.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { apiGetJson } from "./api";

/** Ask Bloom which folder it is keeping its user settings in. */
export async function getUserSettingsFolder(page: Page): Promise<string> {
    const info = await apiGetJson<{ userSettingsFolder: string }>(
        page,
        "common/instanceInfo",
    );
    return info.userSettingsFolder;
}

/**
 * Read one user setting Bloom has saved to the user.config in `folder`, by the name it has in
 * Bloom's Settings.settings (e.g. "PageZoom"), as the text it stands for in the file (XML escapes
 * undone); undefined when there is no such file yet or Bloom has not saved that setting. Only
 * settings serialized as a string are readable this way, which is nearly all of them.
 *
 * This reads the disk rather than asking Bloom, because what a test usually wants to know is what
 * reached the file: that is what the next Bloom to use the folder starts from. Bloom writes some
 * settings a moment after they change (the zoom two seconds after the last change), so poll.
 *
 * Bloom holds the file exclusively for a moment while it saves, and reading it then fails with
 * EBUSY (seen on the nightly runner, 2026-09-10), so a busy file is retried for a few seconds.
 */
export async function readSavedUserSetting(
    folder: string,
    name: string,
): Promise<string | undefined> {
    const file = Path.join(folder, "user.config");
    if (!fs.existsSync(file)) return undefined;
    let xml = "";
    const deadline = Date.now() + 5000;
    for (;;) {
        try {
            xml = fs.readFileSync(file, "utf8");
            break;
        } catch (error) {
            const code = (error as NodeJS.ErrnoException).code;
            if ((code !== "EBUSY" && code !== "EPERM") || Date.now() > deadline)
                throw error;
            await new Promise((resolve) => setTimeout(resolve, 200));
        }
    }
    const match = new RegExp(
        `<setting name="${name}"[^>]*>\\s*<value>([^<]*)</value>`,
    ).exec(xml);
    return match ? decodeXmlText(match[1]) : undefined;
}

/**
 * The text that XML text content stands for: user.config escapes `&`, `<` and `>` (a folder path
 * can hold an ampersand), and a test compares the setting with the value it expects.
 */
function decodeXmlText(text: string): string {
    const named: Record<string, string> = {
        amp: "&",
        lt: "<",
        gt: ">",
        quot: '"',
        apos: "'",
    };
    // One pass, so that the text an escape stands for is never decoded again.
    return text.replace(
        /&(?:#x([0-9a-f]+)|#(\d+)|(amp|lt|gt|quot|apos));/gi,
        (escape, hex, decimal, name) => {
            if (hex) return String.fromCodePoint(parseInt(hex, 16));
            if (decimal) return String.fromCodePoint(Number(decimal));
            return named[name] ?? escape;
        },
    );
}

/**
 * The experimental features (by their ExperimentalFeatures.cs tokens, e.g. "team-collections")
 * that the user.config in `folder` has saved as turned on.
 *
 * Under --e2e Bloom answers "is this feature on?" from its command line alone, never from this
 * setting (see ExperimentalFeatures.TokensOfEnabledFeatures), so the saved setting is the only
 * place a test can see a feature the user turned on in Collection Settings.
 */
export async function getSavedExperimentalFeatures(
    folder: string,
): Promise<string[]> {
    const saved = await readSavedUserSetting(
        folder,
        "EnabledExperimentalFeatures",
    );
    return (saved ?? "")
        .split(",")
        .map((token) => token.trim())
        .filter((token) => token.length > 0);
}
