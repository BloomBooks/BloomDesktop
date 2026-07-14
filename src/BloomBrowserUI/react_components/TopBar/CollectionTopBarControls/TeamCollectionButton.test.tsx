import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../../utils/reactRender";
import { TeamCollectionButton } from "./TeamCollectionButton";
import { TeamCollectionStatus } from "../../../teamCollection/TeamCollectionStatus";

// Tests the status button's label/color state matrix, including the Wave-2 addition: a live
// "Updates Available (N books)" count for cloud Team Collections. teamCollectionApi's
// status-metadata hook is mocked here (rather than exercising the real
// `teamCollection/tcStatusMetadata` endpoint and the experimental-feature gate that guards it)
// to isolate this component's own label/color logic as a unit test.
//
// l10nHooks' useL10n2 is also mocked: the project-wide test-only localizationManager mock
// (vitest.setup.ts) resolves every l10nKey to the key itself, ignoring the `english` fallback and
// dropping format-string params (see the equivalent note in JoinCloudCollectionDialog.test.tsx).
// That makes it impossible to see the count actually land in a %0 substitution through the real
// pipeline, so we substitute a small deterministic stub instead that plainly shows the params it
// was given.
const { mockUseTeamCollectionStatusMetadata } = vi.hoisted(() => ({
    mockUseTeamCollectionStatusMetadata: vi.fn(),
}));

vi.mock("../../../teamCollection/teamCollectionApi", () => ({
    useTeamCollectionStatusMetadata: mockUseTeamCollectionStatusMetadata,
}));

vi.mock("../../l10nHooks", () => ({
    useL10n2: (options: {
        english?: string;
        key: string | null;
        params?: string[];
    }) =>
        options.params && options.params.length > 0
            ? `${options.english} {${options.params.join(",")}}`
            : (options.english ?? options.key ?? ""),
}));

let mountedRoot: HTMLDivElement | undefined;

function renderButton(status: TeamCollectionStatus): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    mountedRoot = container;
    act(() => {
        renderRoot(<TeamCollectionButton status={status} />, container);
    });
    return container;
}

afterEach(() => {
    if (mountedRoot) {
        unmountRoot(mountedRoot);
        mountedRoot.remove();
        mountedRoot = undefined;
    }
    document.body.innerHTML = "";
    mockUseTeamCollectionStatusMetadata.mockReset();
});

describe("TeamCollectionButton", () => {
    it("renders nothing (an empty div) for status None", () => {
        mockUseTeamCollectionStatusMetadata.mockReturnValue({});
        const container = renderButton("None");
        expect(container.querySelector("button")).toBeNull();
    });

    it("shows the plain 'Updates Available' label when the count is unknown (folder Team Collection)", () => {
        // The default mocked state: metadata never populated, matching a folder Team Collection
        // or the cloud-team-collections experimental feature being off.
        mockUseTeamCollectionStatusMetadata.mockReturnValue({});
        const container = renderButton("NewStuff");
        expect(container.textContent).toContain("Updates Available");
        expect(container.textContent).not.toContain("{");
    });

    it("shows the book count in the label when the status metadata provides one", () => {
        mockUseTeamCollectionStatusMetadata.mockReturnValue({
            updatesAvailableCount: 3,
        });
        const container = renderButton("NewStuff");
        expect(container.textContent).toContain("{3}");
    });

    it("does not use the count-driven label for a non-NewStuff status even if a count is present", () => {
        // Sanity check that the count only ever affects the NewStuff label, guarding against a
        // regression that would make e.g. a Nominal or Disconnected button show a stale count.
        mockUseTeamCollectionStatusMetadata.mockReturnValue({
            updatesAvailableCount: 5,
        });
        const container = renderButton("Disconnected");
        expect(container.textContent).not.toContain("{5}");
        expect(container.textContent).toContain("Disconnected");
    });

    it.each([
        ["Nominal", "TeamCollection.TeamCollection"],
        ["Disconnected", "Disconnected"],
        ["Error", "Problems Encountered"],
        ["ClobberPending", "Problems Encountered"],
    ] as [TeamCollectionStatus, string][])(
        "shows the expected label for status %s",
        (status, expectedLabel) => {
            mockUseTeamCollectionStatusMetadata.mockReturnValue({});
            const container = renderButton(status);
            expect(container.textContent).toContain(expectedLabel);
        },
    );
});
