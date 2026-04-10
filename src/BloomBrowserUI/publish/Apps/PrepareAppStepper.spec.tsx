import * as React from "react";
import * as ReactDOM from "react-dom";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { PrepareAppStepper } from "./PrepareAppStepper";

describe("PrepareAppStepper", () => {
    let container: HTMLDivElement | null = null;

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
    });

    afterEach(() => {
        if (container) {
            ReactDOM.unmountComponentAtNode(container);
            container.remove();
            container = null;
        }
    });

    function renderStepper(ui: React.ReactElement): HTMLDivElement {
        if (!container) {
            throw new Error("container not initialized");
        }

        act(() => {
            ReactDOM.render(ui, container);
        });

        return container;
    }

    it("shows active and completed prepare steps without relying on styles", () => {
        const host = renderStepper(
            <PrepareAppStepper
                steps={[
                    {
                        id: "installer-available",
                        complete: true,
                        label: "Get installer",
                    },
                    {
                        id: "rab-installed",
                        complete: false,
                        label: "Run installer",
                    },
                    {
                        id: "build-tools-installed",
                        complete: false,
                        label: "Install build tools",
                    },
                ]}
                activeStepId="rab-installed"
                isBusy={true}
            />,
        );

        expect(
            host
                .querySelector(
                    '[data-testid="prepare-step-installer-available"]',
                )
                ?.getAttribute("data-state"),
        ).toBe("complete");
        expect(
            host
                .querySelector('[data-testid="prepare-step-rab-installed"]')
                ?.getAttribute("data-state"),
        ).toBe("active");
        expect(
            host
                .querySelector(
                    '[data-testid="prepare-step-build-tools-installed"]',
                )
                ?.getAttribute("data-state"),
        ).toBe("pending");
    });

    it("does not render a status summary beneath the prepare steps", () => {
        const host = renderStepper(
            <PrepareAppStepper
                steps={[
                    {
                        id: "installer-available",
                        complete: true,
                        label: "Get installer",
                    },
                    {
                        id: "rab-installed",
                        complete: true,
                        label: "Run installer",
                    },
                ]}
                isBusy={false}
            />,
        );

        expect(
            host.querySelector('[data-testid="prepare-stepper-status"]'),
        ).toBeNull();
    });

    it("wraps each label inside a dedicated width-limited element", () => {
        const host = renderStepper(
            <PrepareAppStepper
                steps={[
                    {
                        id: "build-tools-installed",
                        complete: false,
                        label: "Install build tools for localized layouts",
                    },
                ]}
                isBusy={false}
            />,
        );

        const label = host.querySelector(".prepare-step-label");
        expect(label?.textContent).toBe(
            "Install build tools for localized layouts",
        );
    });
});
