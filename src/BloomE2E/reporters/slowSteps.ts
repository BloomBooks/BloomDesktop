// Prints the slow steps of a run, once, at the end.
//
// A step of an e2e test that takes seconds is worth knowing about, and the reason is never
// obvious from the console: Playwright's own reporters say how long a TEST took, and the times of
// the steps inside it are only in the HTML report, which nobody opens for a passing run. So a step
// that spends seven seconds waiting on something it should not have to wait on stays invisible for
// as long as the test still passes.
//
// This reporter lists every step over a threshold, slowest first, as
//
//     7.1s  tables-core › takes typing in a cell › Type in the first cell
//
// so the worst offender is the last line you read. It reports the steps a spec named with the
// `step` fixture, not Playwright's own internal ones: an `expect` or a locator call is a step too,
// and listing those would bury the ones a person wrote. Set BLOOM_E2E_SLOW_STEP_MS to change the
// threshold; the default is three seconds.
//
// To find out WHY one of those named steps is slow, set BLOOM_E2E_SLOW_STEP_ALL=1 for a run: the
// list then includes Playwright's own steps as well, each shown under the named step it belongs
// to, so the click or the assertion that spent the time is named rather than guessed at.

import type {
    Reporter,
    TestCase,
    TestResult,
    TestStep,
} from "@playwright/test/reporter";
import * as path from "node:path";

/** One slow step, flattened out of the tree Playwright reports. */
interface ISlowStep {
    durationMs: number;
    specName: string;
    testTitle: string;
    stepTitle: string;
}

/** True when this run should list Playwright's own steps as well as the named ones. */
function includeEveryStep(): boolean {
    return !!process.env.BLOOM_E2E_SLOW_STEP_ALL;
}

const kDefaultThresholdMs = 3000;

/** The threshold in milliseconds, from BLOOM_E2E_SLOW_STEP_MS or the default. */
function thresholdMs(): number {
    const asked = Number(process.env.BLOOM_E2E_SLOW_STEP_MS);
    return Number.isFinite(asked) && asked > 0 ? asked : kDefaultThresholdMs;
}

/** "tables-core" from ".../tests/tables-core.spec.ts". */
function specNameOf(test: TestCase): string {
    return path.basename(test.location.file).replace(/\.spec\.[tj]sx?$/i, "");
}

/** Seconds to one decimal place, padded so the times line up. */
function seconds(durationMs: number): string {
    return `${(durationMs / 1000).toFixed(1)}s`.padStart(7);
}

/**
 * Every step a spec named, from the tree of steps in a result. Playwright nests its own steps
 * (`expect`, `locator.click`, a fixture's setup) under and beside them, and `category` is what
 * tells them apart: a step made by `test.step` is "test.step" and nothing else is.
 */
function namedSteps(steps: TestStep[], parentTitle = ""): ISlowStep[] {
    return steps.flatMap((step) => {
        const isNamed = step.category === "test.step";
        // A named step keeps its own title; one of Playwright's is shown under the named step it
        // sits inside, so the line says which of a step's actions took the time.
        const title = isNamed
            ? step.title
            : `${parentTitle} · ${step.category}: ${step.title}`;
        const mine =
            isNamed || (includeEveryStep() && parentTitle)
                ? [
                      {
                          durationMs: step.duration,
                          specName: "",
                          testTitle: "",
                          stepTitle: title,
                      },
                  ]
                : [];
        return [
            ...mine,
            ...namedSteps(step.steps, isNamed ? step.title : parentTitle),
        ];
    });
}

/**
 * Collects the slow steps of a run and prints them at the end. Adding this reporter changes
 * nothing about how the tests run; it only reads what Playwright already recorded.
 */
class SlowStepsReporter implements Reporter {
    private readonly slow: ISlowStep[] = [];

    public onTestEnd(test: TestCase, result: TestResult): void {
        for (const step of namedSteps(result.steps)) {
            if (step.durationMs < thresholdMs()) continue;
            this.slow.push({
                ...step,
                specName: specNameOf(test),
                testTitle: test.title,
            });
        }
    }

    public onEnd(): void {
        const limit = thresholdMs() / 1000;
        if (this.slow.length === 0) {
            console.log(`\nNo test step took as long as ${limit}s.`);
            return;
        }
        this.slow.sort((a, b) => b.durationMs - a.durationMs);
        console.log(`\nSteps over ${limit}s, slowest first:`);
        for (const step of this.slow)
            console.log(
                `${seconds(step.durationMs)}  ${step.specName} › ` +
                    `${step.testTitle} › ${step.stepTitle}`,
            );
    }

    /** Keep the reporter's own output out of the way of the terminal's live updating. */
    public printsToStdio(): boolean {
        return true;
    }
}

export default SlowStepsReporter;
