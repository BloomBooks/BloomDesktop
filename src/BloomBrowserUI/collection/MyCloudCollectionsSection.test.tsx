import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { MyCloudCollectionsSection } from "./MyCloudCollectionsSection";
import {
    ICloudCollectionSummary,
    ISharingLoginState,
} from "../teamCollection/sharingApi";

// Tests the presentational MyCloudCollectionsSection ("Get my Team Collections" sidebar of the
// collection chooser) directly with injected props/callbacks (no network layer), per Wave-1
// scope: shells against mocked endpoints. Covers the signed-out state required by
// Design/CloudTeamCollections/tasks/07-ui-setup.md as well as loading/empty/listing.

let renderedContainer: HTMLDivElement | undefined;

function render(element: React.ReactElement): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(element, container);
    });
    return container;
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
});

const signedOut: ISharingLoginState = { mode: "dev", signedIn: false };
const signedIn: ISharingLoginState = {
    mode: "dev",
    signedIn: true,
    email: "me@example.com",
};

const collectionA: ICloudCollectionSummary = {
    collectionId: "aaa-111",
    name: "Team A Collection",
    role: "admin",
};
const collectionB: ICloudCollectionSummary = {
    collectionId: "bbb-222",
    name: "Team B Collection",
    role: "member",
};

describe("MyCloudCollectionsSection", () => {
    it("shows a sign-in prompt (not a list) when signed out, and the button calls onSignInClick", () => {
        const onSignInClick = vi.fn();
        const container = render(
            <MyCloudCollectionsSection
                loginState={signedOut}
                collections={[collectionA]}
                loading={false}
                onSignInClick={onSignInClick}
                onPullDown={vi.fn()}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-signed-out"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-list"]',
            ),
        ).toBeNull();

        const signInButton = container.querySelector(
            '[data-testid="my-cloud-collections-signin-button"]',
        ) as HTMLButtonElement;
        expect(signInButton).not.toBeNull();
        act(() => signInButton.click());
        expect(onSignInClick).toHaveBeenCalled();
    });

    it("shows a loading indicator while signed in and loading", () => {
        const container = render(
            <MyCloudCollectionsSection
                loginState={signedIn}
                collections={[]}
                loading={true}
                onSignInClick={vi.fn()}
                onPullDown={vi.fn()}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-loading"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-list"]',
            ),
        ).toBeNull();
    });

    it("shows an empty-state message when signed in with no cloud collections", () => {
        const container = render(
            <MyCloudCollectionsSection
                loginState={signedIn}
                collections={[]}
                loading={false}
                onSignInClick={vi.fn()}
                onPullDown={vi.fn()}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="my-cloud-collections-empty"]',
            ),
        ).not.toBeNull();
    });

    it("lists each cloud collection and calls onPullDown with its collectionId", () => {
        const onPullDown = vi.fn();
        const container = render(
            <MyCloudCollectionsSection
                loginState={signedIn}
                collections={[collectionA, collectionB]}
                loading={false}
                onSignInClick={vi.fn()}
                onPullDown={onPullDown}
            />,
        );

        const rows = container.querySelectorAll(
            '[data-testid="my-cloud-collection-row"]',
        );
        expect(rows.length).toBe(2);

        const rowB = Array.from(rows).find(
            (row) =>
                row.getAttribute("data-collection-id") ===
                collectionB.collectionId,
        ) as HTMLElement;
        const pullDownButton = rowB.querySelector(
            '[data-testid="my-cloud-collection-pulldown-button"]',
        ) as HTMLButtonElement;
        act(() => pullDownButton.click());

        expect(onPullDown).toHaveBeenCalledWith(collectionB.collectionId);
    });
});
