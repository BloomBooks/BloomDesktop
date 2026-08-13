import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";

// vi.mock factories are hoisted above the module body, so anything they use must be hoisted too.
const { mockPost, mockPostString, mockCloseDialog, loginState, invitation } =
    vi.hoisted(() => ({
        mockPost: vi.fn(),
        mockPostString: vi.fn(),
        mockCloseDialog: vi.fn(),
        // What the mocked useWatchApiObject (and hence useLoginState) reports. Tests change this to
        // stand in for the C# side broadcasting that the user has signed in.
        loginState: { current: { email: "" } },
        // What the mocked useApiObject reports for account/signInInvitationNeeded.
        invitation: { current: { needed: false } },
    }));

vi.mock("../utils/bloomApi", async (importOriginal) => {
    const actual = await importOriginal<typeof import("../utils/bloomApi")>();
    return {
        ...actual,
        post: mockPost,
        postString: mockPostString,
        useWatchApiObject: () => loginState.current,
        useApiObject: () => invitation.current,
    };
});

// The real BloomDialog renders through a MUI Dialog, which portals its content out of our
// container. These stand-ins keep the dialog's content where the test can find it.
vi.mock("./BloomDialog/BloomDialog", () => ({
    BloomDialog: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogTitle: (props: { title: string }) => <h1>{props.title}</h1>,
    DialogMiddle: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogBottomButtons: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogBottomLeftButtons: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
}));

import {
    SignInInvitationDialog,
    SignInInvitationDialogLauncher,
} from "./signInInvitationDialog";

describe("SignInInvitationDialog", () => {
    let container: HTMLDivElement;

    const render = async () => {
        await act(async () => {
            renderRoot(
                <SignInInvitationDialog
                    closeDialog={mockCloseDialog}
                    propsForBloomDialog={{ open: true }}
                />,
                container,
            );
        });
    };

    const click = (selector: string) => {
        const element = container.querySelector(selector) as HTMLElement;
        if (!element)
            fail(
                `Test cannot proceed: nothing in the dialog matches ${selector}`,
            );
        act(() => {
            element.click();
        });
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        loginState.current = { email: "" };
        mockPost.mockReset();
        mockPostString.mockReset();
        mockCloseDialog.mockReset();
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
    });

    it("asks the server to start the browser login when the user clicks Sign in", async () => {
        await render();
        expect(mockPost).not.toHaveBeenCalled(); // sanity check

        click("#signInToBloomLibrary");

        expect(mockPost).toHaveBeenCalledWith("account/login");
        // Signing in happens out in the browser, so we must stay open until it succeeds.
        expect(mockCloseDialog).not.toHaveBeenCalled();
    });

    it("closes itself once the server reports that the user is signed in", async () => {
        await render();
        expect(mockCloseDialog).not.toHaveBeenCalled(); // sanity check

        loginState.current = { email: "someone@example.com" };
        await render(); // re-render, as the websocket message would

        expect(mockCloseDialog).toHaveBeenCalled();
    });

    it("opens the collaboration documentation when the user clicks the help link", async () => {
        await render();
        expect(mockPostString).not.toHaveBeenCalled(); // sanity check

        click("a");

        expect(mockPostString).toHaveBeenCalledWith(
            "link",
            "https://docs.bloomlibrary.org/collaboration",
        );
    });

    it("just closes when the user says they are not able to sign in yet", async () => {
        await render();

        click("#notAbleToSignInYet");

        expect(mockCloseDialog).toHaveBeenCalled();
        expect(mockPost).not.toHaveBeenCalled();
    });
});

describe("SignInInvitationDialogLauncher", () => {
    let container: HTMLDivElement;

    const render = async () => {
        await act(async () => {
            renderRoot(<SignInInvitationDialogLauncher />, container);
        });
    };

    const dialogIsShowing = () =>
        !!container.querySelector("#signInToBloomLibrary");

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        loginState.current = { email: "" };
        invitation.current = { needed: false };
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
    });

    it("shows nothing when the server says no invitation is needed", async () => {
        await render();

        expect(dialogIsShowing()).toBe(false);
    });

    it("shows the invitation when the server says one is needed", async () => {
        invitation.current = { needed: true };

        await render();

        expect(dialogIsShowing()).toBe(true);
    });

    it("stays closed once dismissed, even though the server still says one was needed", async () => {
        invitation.current = { needed: true };
        await render();
        expect(dialogIsShowing()).toBe(true); // sanity check

        const notYet = container.querySelector(
            "#notAbleToSignInYet",
        ) as HTMLElement;
        act(() => {
            notYet.click();
        });

        expect(dialogIsShowing()).toBe(false);

        await render(); // a re-render must not bring it back
        expect(dialogIsShowing()).toBe(false);
    });
});
