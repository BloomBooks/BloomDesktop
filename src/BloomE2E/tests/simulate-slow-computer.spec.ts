// The "Simulate a slow computer" testing mode (BL-17084): turning it on from the top bar's testing
// menu makes everyday work in Bloom several times slower, and turning it off makes it fast again.
//
// The test times two common operations (changing pages in the Edit tab, and coming back to the
// Edit tab from the Collections tab), turns the mode on, times them again, turns it off and times
// them a third time. Each time runs until the Edit tab is editing the page, which is when a user
// can carry on. The bounds are broad on purpose: the mode slows Bloom only roughly as a slow
// machine would, a CPU cap stretches some work more than other work, and the machine running the
// test is busy with other things. What the test guards is that the mode clearly slows Bloom down,
// does not slow it down absurdly, and comes off.
//
// The mode lasts for the rest of the run of the Bloom that has it, and every test in this file
// shares one Bloom, so this file has one test, and that test turns the mode off before it ends.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    goToPageTimed,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    returnToEditTabTimed,
    setSlowComputerSimulation,
} from "../helpers/workspace";

test.use({
    collectionSpec: { name: "simulate-slow-computer", languages: ["en"] },
});

// How many times each operation is timed; the median of them is what counts.
const kRuns = 5;
// How much slower an operation must get with the mode on, and the most it may get.
const kMinimumSlowdown = 2;
const kMaximumSlowdown = 10;
// With the mode off again, how much slower than at first an operation may still be. Not 1: the
// machine and Bloom's caches are never in quite the same state twice.
const kMostSlowdownAfterTurningOff = 1.5;

/** Run `timedAction` kRuns times and return the median of the durations it reports. */
async function medianMs(timedAction: () => Promise<number>): Promise<number> {
    const durations: number[] = [];
    for (let run = 0; run < kRuns; run++) durations.push(await timedAction());
    durations.sort((a, b) => a - b);
    return durations[Math.floor(kRuns / 2)];
}

test("Simulate a slow computer slows Bloom down several times, and turning it off undoes that [Test Case ID 858]", async ({
    page,
}) => {
    // The mode slows Bloom down enough that the timed runs alone take a minute or two.
    test.setTimeout(600000);

    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image", 2);
    const [first, second] = await getContentPages(page);
    await goToPage(page, first.id);

    // Each returns how long its operation took, and leaves the Edit tab on the first page.
    const operations: Record<string, () => Promise<number>> = {
        "changing pages": async () => {
            const ms = await goToPageTimed(page, second.id);
            await goToPage(page, first.id);
            return ms;
        },
        "coming back to the Edit tab": () => returnToEditTabTimed(page),
    };

    /** The median duration of each operation, after one untimed run of each to warm up. */
    const timeEach = async (): Promise<Record<string, number>> => {
        const result: Record<string, number> = {};
        for (const [name, timedAction] of Object.entries(operations)) {
            await timedAction();
            result[name] = await medianMs(timedAction);
        }
        return result;
    };

    const normal = await timeEach();
    await setSlowComputerSimulation(page, true);
    let slowed!: Record<string, number>;
    try {
        slowed = await timeEach();
    } finally {
        await setSlowComputerSimulation(page, false);
    }
    const afterwards = await timeEach();

    for (const name of Object.keys(operations)) {
        // On the test (the nightly's HTML report) and in the run's output, so that a failure says
        // what the times were.
        const times = `${name}: ${Math.round(normal[name])} ms normally, ${Math.round(slowed[name])} ms slowed, ${Math.round(afterwards[name])} ms after turning it off`;
        test.info().annotations.push({ type: "timing", description: times });
        console.log(times);
    }
    for (const name of Object.keys(operations)) {
        const slowdown = slowed[name] / normal[name];
        const why = `With "Simulate a slow computer" on, ${name} took ${slowdown.toFixed(1)} times as long; it should be between ${kMinimumSlowdown} and ${kMaximumSlowdown}.`;
        expect(slowdown, why).toBeGreaterThanOrEqual(kMinimumSlowdown);
        expect(slowdown, why).toBeLessThanOrEqual(kMaximumSlowdown);
        const leftover = afterwards[name] / normal[name];
        expect(
            leftover,
            `After turning "Simulate a slow computer" off, ${name} still took ${leftover.toFixed(1)} times as long as before it was on.`,
        ).toBeLessThanOrEqual(kMostSlowdownAfterTurningOff);
    }
});
