// How long a flow pass took. The samples are on the window so that a test, or a developer at
// the console, can read them without any instrumentation of its own.

export type FlowPassSample = {
    /** What started the pass: a mutation, the page load, a style change. */
    reason: string;
    ms: number;
    /** performance.now() at the start of the pass. */
    at: number;
};

export const kMetricsPropertyName = "__bloomFlowTextMetrics";
const kMaxSamples = 30;

// A pass runs inside one animation frame, so anything past this shows as a dropped frame.
const kSlowPassMs = 16;

const kMarkPrefix = "bloom-flow";

/** Run one flow pass and record how long it took. */
export function timePass<T>(reason: string, fn: () => T): T {
    const startMark = `${kMarkPrefix}-${reason}-start`;
    const endMark = `${kMarkPrefix}-${reason}-end`;
    const measureName = `${kMarkPrefix}-${reason}`;
    const at = now();

    mark(startMark);
    try {
        return fn();
    } finally {
        mark(endMark);
        measure(measureName, startMark, endMark);
        recordSample({ reason, ms: now() - at, at });
    }
}

/** The recorded passes, oldest first. */
export function getFlowPassSamples(): FlowPassSample[] {
    return getSampleBuffer().slice();
}

function recordSample(sample: FlowPassSample): void {
    const samples = getSampleBuffer();
    samples.push(sample);
    while (samples.length > kMaxSamples) {
        samples.shift();
    }

    if (sample.ms > kSlowPassMs) {
        console.warn(
            `flow text: ${sample.reason} pass took ${sample.ms.toFixed(
                1,
            )}ms, over the ${kSlowPassMs}ms frame budget`,
        );
    }
}

function getSampleBuffer(): FlowPassSample[] {
    const holder = window as unknown as Record<string, FlowPassSample[]>;
    if (!Array.isArray(holder[kMetricsPropertyName])) {
        holder[kMetricsPropertyName] = [];
    }

    return holder[kMetricsPropertyName];
}

function now(): number {
    return window.performance?.now ? window.performance.now() : Date.now();
}

function mark(name: string): void {
    window.performance?.mark?.(name);
}

function measure(name: string, startMark: string, endMark: string): void {
    try {
        window.performance?.measure?.(name, startMark, endMark);
    } catch {
        // A missing mark is not worth failing a pass over.
    }
}
