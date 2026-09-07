// Drives the caption strip Bloom's shell shows during an end-to-end run: the spec file, the test
// title, a clock, and the last three steps with their times. The component is
// src/BloomBrowserUI/app/e2eCaption/E2eStepCaption.tsx, and it installs window.bloomE2eCaption
// only when Bloom was launched with --e2e.
//
// Tests do not call this file directly. The `step` fixture in fixtures/bloomTest.ts wraps it, so a
// spec writes:
//
//     await step("Add a row", async () => { ... });
//
// Everything here is cosmetic and must never fail a test. The shell page can be mid-reload, gone,
// or served by a Bloom.exe too old to have the caption at all, so every call into the page is
// swallowed and bounded by a short timeout.

import { test, type Page } from "@playwright/test";
import * as path from "node:path";

/** How a step ended, in the caption's terms. */
export type CaptionStepStatus = "passed" | "failed" | "fixme";

/** One instruction for the caption. Kept a plain object so it can cross into the page. */
type CaptionCommand =
    | {
          kind: "begin";
          specName: string;
          testTitle: string;
          totalSteps?: number;
      }
    | { kind: "step"; title: string }
    | { kind: "end"; status: CaptionStepStatus; durationMs?: number }
    | { kind: "finish" };

/** Options for a single step. */
export interface IStepOptions {
    /**
     * Record the step as skipped and do not run its body. The caption shows it in italics with a
     * dash, and the Playwright report shows a step that did nothing.
     */
    fixme?: boolean;
}

/** What the `step` fixture hands a test. Returns whatever the body returns. */
export type StepFunction = <T>(
    title: string,
    body: () => T | Promise<T>,
    options?: IStepOptions,
) => Promise<T | undefined>;

// A call into the page is cosmetic, so it gets a short leash: a shell that is navigating can leave
// an evaluate pending for as long as the navigation takes, and no test should wait on that.
const kCaptionCallTimeoutMs = 2000;

/**
 * Send one instruction to the caption in the shell. Never throws, and never waits long.
 * `getPage` is a function rather than a page because bloomApp.restart() replaces the page.
 */
async function sendToCaption(
    getPage: () => Page,
    command: CaptionCommand,
): Promise<void> {
    const timedOut = new Promise<void>((resolve) =>
        setTimeout(resolve, kCaptionCallTimeoutMs),
    );
    const sent = getPage()
        .evaluate((instruction: CaptionCommand) => {
            const api = (
                window as unknown as {
                    bloomE2eCaption?: {
                        begin(
                            specName: string,
                            testTitle: string,
                            totalSteps?: number,
                        ): void;
                        step(title: string): void;
                        end(status: string, durationMs?: number): void;
                        finish(): void;
                    };
                }
            ).bloomE2eCaption;
            // No caption here: a normal run, or a Bloom.exe older than this feature. Nothing to do.
            if (!api) return;
            switch (instruction.kind) {
                case "begin":
                    api.begin(
                        instruction.specName,
                        instruction.testTitle,
                        instruction.totalSteps,
                    );
                    break;
                case "step":
                    api.step(instruction.title);
                    break;
                case "end":
                    api.end(instruction.status, instruction.durationMs);
                    break;
                case "finish":
                    api.finish();
                    break;
            }
        }, command)
        .then(
            () => undefined,
            () => undefined,
        );
    await Promise.race([sent, timedOut]);
}

/** The spec name the caption shows: "tables-core" from ".../tests/tables-core.spec.ts". */
export function specNameFromFile(specFile: string): string {
    return path.basename(specFile).replace(/\.spec\.[tj]sx?$/i, "");
}

/** Tell the caption a test has started. The fixture calls this; a spec does not. */
export async function beginCaption(
    getPage: () => Page,
    specFile: string,
    testTitle: string,
): Promise<void> {
    await sendToCaption(getPage, {
        kind: "begin",
        specName: specNameFromFile(specFile),
        testTitle,
    });
}

/** Tell the caption the test is over, so its clock stops. */
export async function finishCaption(getPage: () => Page): Promise<void> {
    await sendToCaption(getPage, { kind: "finish" });
}

/**
 * Run one step of a test: `test.step`, so the Playwright report keeps its nesting, and the caption
 * in the shell, so whoever is watching the window can see what the test is doing.
 */
export async function runStep<T>(
    getPage: () => Page,
    title: string,
    body: () => T | Promise<T>,
    options?: IStepOptions,
): Promise<T | undefined> {
    await sendToCaption(getPage, { kind: "step", title });
    if (options?.fixme) {
        await sendToCaption(getPage, { kind: "end", status: "fixme" });
        return undefined;
    }
    const startedAt = Date.now();
    try {
        const result = await test.step(title, async () => await body());
        await sendToCaption(getPage, {
            kind: "end",
            status: "passed",
            durationMs: Date.now() - startedAt,
        });
        return result;
    } catch (error) {
        await sendToCaption(getPage, {
            kind: "end",
            status: "failed",
            durationMs: Date.now() - startedAt,
        });
        throw error;
    }
}
