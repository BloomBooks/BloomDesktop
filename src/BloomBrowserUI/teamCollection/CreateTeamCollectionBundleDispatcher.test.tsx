import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { normalDialogEnvironmentForStorybook } from "../react_components/BloomDialog/BloomDialogPlumbing";
import { CreateTeamCollectionBundleDispatcher } from "./CreateTeamCollection";

// Regression test for the bug this dispatcher fixes: "createTeamCollectionDialogBundle" is one
// shared bundle/entry hosting three top-level dialogs (folder create, cloud create, sign-in),
// but WireUpForWinforms sets a single global, so at most one component per bundle may call it.
// Before this dispatcher existed, the folder and cloud dialogs each called WireUpForWinforms
// directly, and whichever one's module-scope call ran last silently won -- breaking the other
// dialog (in practice, the folder-TC "Create Team Collection" dialog could no longer open).
// This test proves the dispatcher renders the right component for every dialogKind C# can send,
// including the case where dialogKind is omitted (must still be the folder dialog, since that's
// the one caller that predates the prop).

let mountedRoot: HTMLDivElement | undefined;

function render(element: React.ReactElement): HTMLDivElement {
    const root = document.createElement("div");
    document.body.appendChild(root);
    mountedRoot = root;
    act(() => {
        renderRoot(element, root);
    });
    return root;
}

afterEach(() => {
    if (mountedRoot) {
        unmountRoot(mountedRoot);
        mountedRoot.remove();
        mountedRoot = undefined;
    }
    // BloomDialog/MUI Dialog portal their content directly onto document.body.
    document.body.innerHTML = "";
});

describe("CreateTeamCollectionBundleDispatcher", () => {
    it('renders the folder create dialog when dialogKind is "folder"', () => {
        render(
            <CreateTeamCollectionBundleDispatcher
                dialogKind="folder"
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />,
        );
        expect(
            document.querySelector('[id="create-and-restart"]'),
        ).not.toBeNull();
    });

    it("renders the folder create dialog when dialogKind is omitted (default)", () => {
        render(
            <CreateTeamCollectionBundleDispatcher
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />,
        );
        expect(
            document.querySelector('[id="create-and-restart"]'),
        ).not.toBeNull();
    });

    it('renders the cloud create dialog when dialogKind is "cloud"', () => {
        render(
            <CreateTeamCollectionBundleDispatcher
                dialogKind="cloud"
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />,
        );
        expect(
            document.querySelector('[data-testid="cloud-create-signin-step"]'),
        ).not.toBeNull();
        expect(document.querySelector('[id="create-and-restart"]')).toBeNull();
    });

    it('renders the sign-in dialog when dialogKind is "signIn"', () => {
        render(
            <CreateTeamCollectionBundleDispatcher
                dialogKind="signIn"
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />,
        );
        expect(
            document.querySelector('[data-testid="signin-dev-form"]'),
        ).not.toBeNull();
        expect(document.querySelector('[id="create-and-restart"]')).toBeNull();
    });
});
