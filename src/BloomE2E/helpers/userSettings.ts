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
 * Bloom's Settings.settings (e.g. "PageZoom"), as the string in the file; undefined when there is
 * no such file yet or Bloom has not saved that setting. Only settings serialized as a string are
 * readable this way, which is nearly all of them.
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
    return match ? match[1] : undefined;
}
