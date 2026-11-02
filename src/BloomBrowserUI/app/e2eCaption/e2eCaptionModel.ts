// The state behind the end-to-end step caption, kept separate from the React component so it can
// be unit tested without a DOM. See E2eStepCaption.tsx for what it looks like, and
// src/BloomE2E/helpers/caption.ts for the test-side helper that drives it.

/** What has become of a step. A step is "running" until the helper reports how it ended. */
export type E2eStepStatus = "running" | "passed" | "failed" | "fixme";

/** One step line in the caption. */
export interface IE2eStep {
    title: string;
    status: E2eStepStatus;
    /** Date.now() when the step started. */
    startedAt: number;
    /** How long the step took, once it has finished. */
    durationMs?: number;
}

/** Everything the caption draws. */
export interface IE2eCaptionState {
    /** The spec file the running test came from, e.g. "tables-core". Empty means nothing to show. */
    specName: string;
    /** The test's title. */
    testTitle: string;
    /** Date.now() when the test started. */
    testStartedAt: number;
    /** How long the whole test took, once it has finished; undefined while it is still running. */
    testDurationMs?: number;
    /** How many steps the test will run, when that is known ahead of time. */
    totalSteps?: number;
    steps: IE2eStep[];
}

/** A caption with nothing in it: what the component holds before any test starts. */
export const emptyE2eCaptionState: IE2eCaptionState = {
    specName: "",
    testTitle: "",
    testStartedAt: 0,
    steps: [],
};

/** How many step lines the strip shows at once. */
export const kVisibleStepCount = 3;

/** A step whose time passes this turns its timer amber: in this suite that usually means a wait
 * is about to time out. */
export const kSlowStepMs = 10000;

/**
 * Start a test. Everything from the previous test is dropped, because the strip shows one test.
 */
export const beginTest = (
    specName: string,
    testTitle: string,
    now: number,
    totalSteps?: number,
): IE2eCaptionState => ({
    specName,
    testTitle,
    testStartedAt: now,
    totalSteps,
    steps: [],
});

/**
 * Start a step. Any step still marked running is taken to have passed, so a caller that forgets
 * to end one does not leave two pulsing lines.
 */
export const beginStep = (
    state: IE2eCaptionState,
    title: string,
    now: number,
): IE2eCaptionState => ({
    ...state,
    steps: [
        ...closeRunningSteps(state.steps, now),
        { title, status: "running", startedAt: now },
    ],
});

/**
 * Report how the current step ended. `durationMs` is the test side's own measurement; when it is
 * not given we fall back to the clock we started.
 */
export const endStep = (
    state: IE2eCaptionState,
    status: Exclude<E2eStepStatus, "running">,
    now: number,
    durationMs?: number,
): IE2eCaptionState => {
    const lastRunning = lastRunningIndex(state.steps);
    if (lastRunning < 0) return state;
    const steps = state.steps.slice();
    const step = steps[lastRunning];
    steps[lastRunning] = {
        ...step,
        status,
        durationMs: durationMs ?? now - step.startedAt,
    };
    return { ...state, steps };
};

/**
 * The test is over. The clock stops, and whatever the last step was stays on screen.
 */
export const finishTest = (
    state: IE2eCaptionState,
    now: number,
): IE2eCaptionState => ({
    ...state,
    steps: closeRunningSteps(state.steps, now),
    testDurationMs: state.testStartedAt ? now - state.testStartedAt : undefined,
});

/** The last three steps: the window the strip shows. */
export const visibleSteps = (state: IE2eCaptionState): IE2eStep[] =>
    state.steps.slice(Math.max(0, state.steps.length - kVisibleStepCount));

/**
 * The right-hand side of the header: "4 / 10" when the total is known, "4" when it is not, and
 * nothing at all before the first step.
 */
export const stepCounterText = (state: IE2eCaptionState): string => {
    if (state.steps.length === 0) return "";
    if (state.totalSteps === undefined) return `${state.steps.length}`;
    return `${state.steps.length} / ${state.totalSteps}`;
};

/** A step's own time, as the strip prints it: "2.1s" under ten seconds, "11s" over. */
export const formatStepTime = (ms: number): string =>
    ms < kSlowStepMs
        ? `${(ms / 1000).toFixed(1)}s`
        : `${Math.round(ms / 1000)}s`;

/** The header clock: mm:ss of the test's elapsed time. */
export const formatClock = (ms: number): string => {
    const seconds = Math.max(0, Math.floor(ms / 1000));
    const minutes = Math.floor(seconds / 60);
    return `${String(minutes).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
};

/** How long the test has been going: frozen once it has finished. */
export const testElapsedMs = (state: IE2eCaptionState, now: number): number => {
    if (state.testDurationMs !== undefined) return state.testDurationMs;
    if (!state.testStartedAt) return 0;
    return now - state.testStartedAt;
};

/** How long a step has been going, or took. */
export const stepElapsedMs = (step: IE2eStep, now: number): number =>
    step.durationMs ?? now - step.startedAt;

const lastRunningIndex = (steps: IE2eStep[]): number => {
    for (let i = steps.length - 1; i >= 0; i--) {
        if (steps[i].status === "running") return i;
    }
    return -1;
};

const closeRunningSteps = (steps: IE2eStep[], now: number): IE2eStep[] =>
    steps.map((step) =>
        step.status === "running"
            ? {
                  ...step,
                  status: "passed" as const,
                  durationMs: now - step.startedAt,
              }
            : step,
    );
