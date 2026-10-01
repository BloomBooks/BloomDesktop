// A caption strip at the middle bottom of the Bloom shell saying what an end-to-end test is
// doing: the spec file and test title, a clock, and the last three steps with their times.
//
// It exists only while Bloom runs under --e2e (see common/instanceInfo's runningE2eTests, set in
// CommonApi.cs). In any other run this component renders nothing and installs nothing on window,
// so a person using Bloom can neither see it nor find it in the DOM.
//
// The test side drives it through window.bloomE2eCaption, from src/BloomE2E/helpers/caption.ts.
// It is cosmetic: nothing here may throw into the page, and a test whose page reloads mid-run
// simply loses the caption until the next call.

import * as React from "react";
import { css } from "@emotion/react";
import { useApiObject } from "../../utils/bloomApi";
import {
    beginStep,
    beginTest,
    emptyE2eCaptionState,
    endStep,
    finishTest,
    formatClock,
    formatStepTime,
    IE2eCaptionState,
    IE2eStep,
    kSlowStepMs,
    stepCounterText,
    stepElapsedMs,
    testElapsedMs,
    visibleSteps,
} from "./e2eCaptionModel";

/** The imperative API a test drives the caption with, over CDP. */
export interface IE2eCaptionApi {
    /** Start a test: its spec file, its title, and how many steps it will run if that is known. */
    begin(specName: string, testTitle: string, totalSteps?: number): void;
    /** Start a step. */
    step(title: string): void;
    /** Say how the current step ended, and how long the test side measured it taking. */
    end(status: "passed" | "failed" | "fixme", durationMs?: number): void;
    /** The test is over: stop the clock and leave the last steps showing. */
    finish(): void;
}

declare global {
    interface Window {
        bloomE2eCaption?: IE2eCaptionApi;
    }
}

// How often the live timers repaint. Fast enough that the tenths in "2.1s" move, cheap enough
// that it costs nothing next to what the test itself is doing.
const kTickMs = 100;

const strip = "rgba(22, 32, 36, 0.86)";
const stripInk = "#f2f6f7";
const stripDim = "rgba(242, 246, 247, 0.55)";
const okColor = "#7dd6a4";
const failColor = "#ff8f80";
const slowColor = "#ffd08a";
const monospace = `Consolas, "Roboto Mono", "Courier New", monospace`;

export const E2eStepCaption: React.FunctionComponent = () => {
    const instanceInfo = useApiObject<{ runningE2eTests?: boolean }>(
        "common/instanceInfo",
        {},
    );
    const runningE2eTests = !!instanceInfo.runningE2eTests;
    const [state, setState] =
        React.useState<IE2eCaptionState>(emptyE2eCaptionState);
    const [now, setNow] = React.useState<number>(() => Date.now());

    // Install the API the test drives. Only under --e2e, so nothing about the caption exists on
    // window in a normal run.
    React.useEffect(() => {
        if (!runningE2eTests) return undefined;
        const api: IE2eCaptionApi = {
            begin: (specName, testTitle, totalSteps) =>
                setState(
                    beginTest(specName, testTitle, Date.now(), totalSteps),
                ),
            step: (title) =>
                setState((previous) => beginStep(previous, title, Date.now())),
            end: (status, durationMs) =>
                setState((previous) =>
                    endStep(previous, status, Date.now(), durationMs),
                ),
            finish: () =>
                setState((previous) => finishTest(previous, Date.now())),
        };
        window.bloomE2eCaption = api;
        return () => {
            if (window.bloomE2eCaption === api) delete window.bloomE2eCaption;
        };
    }, [runningE2eTests]);

    // Tick the live timers while a test is running. A finished test keeps its numbers, so we stop.
    const testIsRunning =
        !!state.testStartedAt && state.testDurationMs === undefined;
    React.useEffect(() => {
        if (!runningE2eTests || !testIsRunning) return undefined;
        const timer = window.setInterval(() => setNow(Date.now()), kTickMs);
        return () => window.clearInterval(timer);
    }, [runningE2eTests, testIsRunning]);

    if (!runningE2eTests || !state.testStartedAt) return null;

    const counter = stepCounterText(state);
    const shown = visibleSteps(state);

    return (
        <div
            data-testid="e2e-step-caption"
            aria-live="polite"
            css={css`
                position: fixed;
                left: 50%;
                bottom: 14px;
                transform: translateX(-50%);
                width: 560px;
                max-width: calc(100% - 40px);
                /* Never intercept a click: the test is driving the UI underneath it. */
                pointer-events: none;
                z-index: 2147483000;
                background: ${strip};
                color: ${stripInk};
                border-radius: 6px;
                padding: 7px 12px 8px;
                box-shadow: 0 4px 16px rgba(0, 0, 0, 0.35);
                font-size: 12.5px;
                line-height: 18px;
                backdrop-filter: blur(3px);
            `}
        >
            <div
                css={css`
                    display: flex;
                    justify-content: space-between;
                    gap: 12px;
                    font: 500 11px/16px ${monospace};
                    color: ${stripDim};
                    letter-spacing: 0.02em;
                    margin-bottom: 3px;
                `}
            >
                <span
                    css={css`
                        overflow: hidden;
                        text-overflow: ellipsis;
                        white-space: nowrap;
                    `}
                >
                    <b
                        css={css`
                            color: ${stripInk};
                            font-weight: 500;
                        `}
                    >
                        {state.specName}
                    </b>
                    {state.testTitle ? ` · ${state.testTitle}` : ""}
                </span>
                <span
                    css={css`
                        white-space: nowrap;
                        font-variant-numeric: tabular-nums;
                    `}
                >
                    {counter ? `${counter} · ` : ""}
                    {formatClock(testElapsedMs(state, now))}
                </span>
            </div>
            <div
                css={css`
                    height: 54px;
                    overflow: hidden;
                    display: flex;
                    flex-direction: column;
                    justify-content: flex-end;
                `}
            >
                {shown.map((step, index) => (
                    <StepLine
                        // Steps scroll up a fixed window, so identify a line by where the step
                        // sits in the whole run, not by its title, which can repeat.
                        key={state.steps.length - shown.length + index}
                        step={step}
                        now={now}
                    />
                ))}
            </div>
        </div>
    );
};

/** One step line: marker, title, and the step's time. */
const StepLine: React.FunctionComponent<{
    step: IE2eStep;
    now: number;
}> = (props) => {
    const running = props.step.status === "running";
    const failed = props.step.status === "failed";
    const fixme = props.step.status === "fixme";
    const elapsed = stepElapsedMs(props.step, props.now);

    let markColor = okColor;
    if (running) markColor = stripInk;
    if (failed) markColor = failColor;
    if (fixme) markColor = stripDim;

    let mark = "✓";
    if (running) mark = "▸";
    if (failed) mark = "✗";
    if (fixme) mark = "–";

    let lineColor = stripDim;
    if (running) lineColor = stripInk;
    if (failed) lineColor = failColor;

    let timeColor = stripDim;
    if (running) timeColor = stripInk;
    if (!running && elapsed > kSlowStepMs) timeColor = slowColor;

    return (
        <div
            css={css`
                display: grid;
                grid-template-columns: 16px 1fr auto;
                gap: 8px;
                align-items: baseline;
                white-space: nowrap;
                overflow: hidden;
                color: ${lineColor};
                font-style: ${fixme ? "italic" : "normal"};
            `}
        >
            <span
                css={css`
                    font-family: ${monospace};
                    text-align: center;
                    color: ${markColor};
                    ${running
                        ? "animation: bloomE2eCaptionPulse 1.1s ease-in-out infinite;"
                        : ""}
                    @keyframes bloomE2eCaptionPulse {
                        50% {
                            opacity: 0.35;
                        }
                    }
                    @media (prefers-reduced-motion: reduce) {
                        animation: none;
                    }
                `}
            >
                {mark}
            </span>
            <span
                css={css`
                    overflow: hidden;
                    text-overflow: ellipsis;
                `}
            >
                {props.step.title}
            </span>
            <span
                css={css`
                    font: 11.5px/18px ${monospace};
                    font-variant-numeric: tabular-nums;
                    color: ${timeColor};
                `}
            >
                {fixme ? "–" : formatStepTime(elapsed)}
            </span>
        </div>
    );
};
