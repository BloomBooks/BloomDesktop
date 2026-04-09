import * as React from "react";
import * as ReactDOM from "react-dom";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
    ActionLogAccordion,
    useActionLogController,
} from "./ActionLogAccordion";

const { scrollToLastErrorMock } = vi.hoisted(() => {
    return {
        scrollToLastErrorMock: vi.fn(),
    };
});

vi.mock("../../react_components/Progress/progressBox", async () => {
    const React = await import("react");

    const MockProgressBox = React.forwardRef(
        (
            props: {
                messages?: Array<JSX.Element>;
            },
            ref: React.ForwardedRef<{
                clear: () => void;
                scrollToLastError: () => void;
            }>,
        ) => {
            React.useImperativeHandle(ref, () => ({
                clear: () => undefined,
                scrollToLastError: () => scrollToLastErrorMock(),
            }));

            return <div data-testid="mock-progress-box">{props.messages}</div>;
        },
    );
    MockProgressBox.displayName = "MockProgressBox";

    return {
        ProgressBox: MockProgressBox,
    };
});

const TestHarness: React.FunctionComponent = () => {
    const setupLog = useActionLogController();
    const buildLog = useActionLogController();
    const [setupActive, setSetupActive] = React.useState(false);
    const [buildActive, setBuildActive] = React.useState(false);

    return (
        <div>
            <button
                data-testid="activate-setup"
                onClick={() => setSetupActive(true)}
            >
                activate setup
            </button>
            <button
                data-testid="activate-build"
                onClick={() => setBuildActive(true)}
            >
                activate build
            </button>
            <button
                data-testid="add-setup-message"
                onClick={() =>
                    setupLog.setMessages([
                        <p key="setup-message">Setup message</p>,
                    ])
                }
            >
                add setup message
            </button>
            <button
                data-testid="clear-setup-message"
                onClick={() => setupLog.clear()}
            >
                clear setup message
            </button>
            <button
                data-testid="add-build-message"
                onClick={() =>
                    buildLog.setMessages([
                        <p key="build-message">Build message</p>,
                    ])
                }
            >
                add build message
            </button>
            <button
                data-testid="open-setup-error"
                onClick={() => {
                    setupLog.setMessages([
                        <p key="setup-error" data-progress-severity="error">
                            Setup error
                        </p>,
                    ]);
                    setupLog.openForError();
                }}
            >
                open setup error
            </button>
            <ActionLogAccordion
                controller={setupLog}
                isActive={setupActive}
                dataTestId="setup-log"
            />
            <ActionLogAccordion
                controller={buildLog}
                isActive={buildActive}
                dataTestId="build-log"
            />
        </div>
    );
};

describe("ActionLogAccordion", () => {
    let container: HTMLDivElement | null = null;

    const renderHarness = () => {
        if (!container) {
            throw new Error("render container not initialized");
        }

        act(() => {
            ReactDOM.render(<TestHarness />, container);
        });

        return container;
    };

    const click = (target: Element | null) => {
        if (!target) {
            throw new Error("target not found");
        }

        act(() => {
            target.dispatchEvent(
                new MouseEvent("click", {
                    bubbles: true,
                    cancelable: true,
                }),
            );
        });
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        scrollToLastErrorMock.mockClear();
        vi.useFakeTimers();
    });

    afterEach(() => {
        if (container) {
            ReactDOM.unmountComponentAtNode(container);
            container.remove();
            container = null;
        }
        vi.runOnlyPendingTimers();
        vi.useRealTimers();
        scrollToLastErrorMock.mockClear();
    });

    it("hides each step log until that step has output", () => {
        const host = renderHarness();

        expect(host.querySelector('[data-testid="setup-log"]')).toBeNull();
        expect(host.querySelector('[data-testid="build-log"]')).toBeNull();

        click(host.querySelector('[data-testid="add-setup-message"]'));

        expect(host.querySelector('[data-testid="setup-log"]')).not.toBeNull();
        expect(host.querySelector('[data-testid="build-log"]')).toBeNull();
        expect(
            host
                .querySelector('[data-testid="setup-log-summary"]')
                ?.getAttribute("aria-expanded"),
        ).toBe("false");
    });

    it("keeps the listener mounted while a step is active before any messages arrive", () => {
        const host = renderHarness();

        click(host.querySelector('[data-testid="activate-build"]'));

        expect(host.querySelector('[data-testid="build-log"]')).toBeNull();
        expect(
            host.querySelector('[data-testid="build-log-listener"]'),
        ).not.toBeNull();
    });

    it("keeps step accordions independent and opens only the errored step", () => {
        const host = renderHarness();

        click(host.querySelector('[data-testid="activate-build"]'));
        click(host.querySelector('[data-testid="add-build-message"]'));
        click(host.querySelector('[data-testid="open-setup-error"]'));

        act(() => {
            vi.runAllTimers();
        });

        expect(
            host
                .querySelector('[data-testid="setup-log-summary"]')
                ?.getAttribute("aria-expanded"),
        ).toBe("true");
        expect(
            host
                .querySelector('[data-testid="build-log-summary"]')
                ?.getAttribute("aria-expanded"),
        ).toBe("false");
        expect(scrollToLastErrorMock).toHaveBeenCalledTimes(1);
    });

    it("hides a step log again when that step is cleared", () => {
        const host = renderHarness();

        click(host.querySelector('[data-testid="add-setup-message"]'));
        expect(host.querySelector('[data-testid="setup-log"]')).not.toBeNull();

        click(host.querySelector('[data-testid="clear-setup-message"]'));

        expect(host.querySelector('[data-testid="setup-log"]')).toBeNull();
    });
});
