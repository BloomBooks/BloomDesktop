import { describe, expect, it } from "vitest";
import {
    arePrepareStepsComplete,
    getPrepareStepIdForStage,
    normalizeStatus,
} from "./appBuilderShared";

describe("appBuilderShared prepare steps", () => {
    it("normalizes prepare steps from PascalCase API payloads", () => {
        const status = normalizeStatus({
            PrepareSteps: [
                { Id: "installer-available", Complete: true },
                { Id: "rab-installed", Complete: false },
            ],
        });

        expect(status.prepareSteps).toEqual([
            { id: "installer-available", complete: true },
            { id: "rab-installed", complete: false },
        ]);
    });

    it("maps setup websocket stages onto prepare step ids", () => {
        expect(getPrepareStepIdForStage("setup", "running-installer")).toBe(
            "rab-installed",
        );
        expect(
            getPrepareStepIdForStage("setup", "installing-build-tools"),
        ).toBe("build-tools-installed");
        expect(
            getPrepareStepIdForStage("build", "installing-build-tools"),
        ).toBeUndefined();
    });

    it("treats the prepare checklist as ready only when every step is complete", () => {
        expect(
            arePrepareStepsComplete([
                { id: "installer-available", complete: true },
                { id: "rab-installed", complete: true },
            ]),
        ).toBe(true);
        expect(
            arePrepareStepsComplete([
                { id: "installer-available", complete: true },
                { id: "rab-installed", complete: false },
            ]),
        ).toBe(false);
    });
});
