import { act } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderTestRoot as render, unmountTestRoot } from "../utils/testRender";
import { CreateCloudTeamCollectionBody } from "./CreateCloudTeamCollection";
import { ISharingLoginState } from "./sharingApi";

// Tests the presentational CreateCloudTeamCollectionBody directly with injected
// props/callbacks (no network layer), per Wave-1 scope: shells against mocked endpoints.
// Covers the step gating described in Design/CloudTeamCollections/tasks/07-ui-setup.md:
// sign-in (local-mode form) -> immutable-name acknowledgement -> initial Send progress.

// React tracks the previous value of native inputs internally; setting .value directly and
// dispatching a plain Event doesn't trigger React's onChange. Standard workaround (there is no
// @testing-library/react / user-event dependency in this project).
function setNativeValue(element: HTMLInputElement, value: string) {
    const setter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype,
        "value",
    )?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
    element.dispatchEvent(new Event("input", { bubbles: true }));
}

const signedOutLocal: ISharingLoginState = { mode: "local", signedIn: false };
const signedOutCloud: ISharingLoginState = { mode: "cloud", signedIn: false };
const signedIn: ISharingLoginState = {
    mode: "local",
    signedIn: true,
    email: "me@example.com",
    emailVerified: true,
};

function baseProps(
    overrides: Partial<
        React.ComponentProps<typeof CreateCloudTeamCollectionBody>
    > = {},
) {
    return {
        loginState: signedOutLocal,
        collectionName: "My Collection",
        localEmail: "",
        localPassword: "",
        onLocalEmailChange: vi.fn(),
        onLocalPasswordChange: vi.fn(),
        onLocalSignIn: vi.fn(),
        signInSubmitAttempts: 0,
        signInError: undefined,
        onCloudSignInClick: vi.fn(),
        nameAcknowledged: false,
        onAcknowledgeNameChange: vi.fn(),
        sendState: "notStarted" as const,
        sendError: undefined,
        onStartSend: vi.fn(),
        onRetrySend: vi.fn(),
        ...overrides,
    };
}

describe("CreateCloudTeamCollectionBody", () => {
    it("shows the local-mode sign-in form when signed out in local mode", () => {
        const container = render(
            <CreateCloudTeamCollectionBody {...baseProps()} />,
        );

        expect(
            container.querySelector('[data-testid="cloud-create-signin-step"]'),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="cloud-create-signin-email"]',
            ),
        ).not.toBeNull();
        // Gating: the confirm/name-acknowledgement step must not be reachable yet.
        expect(
            container.querySelector(
                '[data-testid="cloud-create-confirm-step"]',
            ),
        ).toBeNull();
    });

    it("shows the cloud-mode sign-in button (no email/password fields) when signed out in cloud mode", () => {
        const onCloudSignInClick = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    loginState: signedOutCloud,
                    onCloudSignInClick,
                })}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="cloud-create-signin-email"]',
            ),
        ).toBeNull();
        const button = container.querySelector(
            '[data-testid="cloud-create-cloud-signin-button"]',
        ) as HTMLButtonElement;
        expect(button).not.toBeNull();

        act(() => button.click());
        expect(onCloudSignInClick).toHaveBeenCalled();
    });

    it("reports typed email/password changes to the container via onLocalEmailChange/onLocalPasswordChange", () => {
        const onLocalEmailChange = vi.fn();
        const onLocalPasswordChange = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({ onLocalEmailChange, onLocalPasswordChange })}
            />,
        );

        const emailInput = container.querySelector(
            '[data-testid="cloud-create-signin-email"] input',
        ) as HTMLInputElement;
        act(() => setNativeValue(emailInput, "me@example.com"));
        expect(onLocalEmailChange).toHaveBeenCalledWith("me@example.com");

        const passwordInput = container.querySelector(
            '[data-testid="cloud-create-signin-password"] input',
        ) as HTMLInputElement;
        act(() => setNativeValue(passwordInput, "secret"));
        expect(onLocalPasswordChange).toHaveBeenCalledWith("secret");
    });

    it("calls onLocalSignIn with the entered credentials via the container callback", () => {
        const onLocalSignIn = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    localEmail: "me@example.com",
                    localPassword: "secret",
                    onLocalSignIn,
                })}
            />,
        );

        const button = container.querySelector(
            '[data-testid="cloud-create-signin-button"]',
        ) as HTMLButtonElement;
        act(() => button.click());

        expect(onLocalSignIn).toHaveBeenCalled();
    });

    it("shows a sign-in error when provided", () => {
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({ signInError: "Invalid credentials" })}
            />,
        );

        const errorEl = container.querySelector(
            '[data-testid="cloud-create-signin-error"]',
        );
        expect(errorEl).not.toBeNull();
        expect(errorEl?.textContent).toContain("Invalid credentials");
    });

    it("gates the Share button on the immutable-name acknowledgement checkbox once signed in", () => {
        const onStartSend = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    loginState: signedIn,
                    nameAcknowledged: false,
                    onStartSend,
                })}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="cloud-create-confirm-step"]',
            ),
        ).not.toBeNull();
        const shareButton = container.querySelector(
            '[data-testid="cloud-create-share-button"]',
        ) as HTMLButtonElement;
        expect(shareButton.disabled).toBe(true);

        act(() => shareButton.click());
        expect(onStartSend).not.toHaveBeenCalled();
    });

    it("enables the Share button once the name is acknowledged, and starting send calls onStartSend", () => {
        const onStartSend = vi.fn();
        const onAcknowledgeNameChange = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    loginState: signedIn,
                    nameAcknowledged: false,
                    onAcknowledgeNameChange,
                    onStartSend,
                })}
            />,
        );

        const checkbox = container.querySelector(
            "#cloud-create-name-ack-checkbox",
        ) as HTMLInputElement;
        expect(checkbox).not.toBeNull();
        expect(checkbox.checked).toBe(false);
        act(() => checkbox.click());
        expect(onAcknowledgeNameChange).toHaveBeenCalledWith(true);

        // Re-render with the acknowledgement now true, as the container would after the callback.
        unmountTestRoot(container);
        const container2 = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    loginState: signedIn,
                    nameAcknowledged: true,
                    onStartSend,
                })}
            />,
        );
        const shareButton = container2.querySelector(
            '[data-testid="cloud-create-share-button"]',
        ) as HTMLButtonElement;
        expect(shareButton.disabled).toBe(false);
        act(() => shareButton.click());
        expect(onStartSend).toHaveBeenCalled();
    });

    it("shows send progress while sending", () => {
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({ loginState: signedIn, sendState: "sending" })}
            />,
        );

        expect(
            container.querySelector(
                '[data-testid="cloud-create-sending-step"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector('[data-testid="cloud-create-progress"]'),
        ).not.toBeNull();
    });

    it("shows an error and a retry button when the send fails", () => {
        const onRetrySend = vi.fn();
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({
                    loginState: signedIn,
                    sendState: "error",
                    sendError: "Network error",
                    onRetrySend,
                })}
            />,
        );

        const errorEl = container.querySelector(
            '[data-testid="cloud-create-error"]',
        );
        expect(errorEl?.textContent).toContain("Network error");
        const retryButton = container.querySelector(
            '[data-testid="cloud-create-retry-button"]',
        ) as HTMLButtonElement;
        act(() => retryButton.click());
        expect(onRetrySend).toHaveBeenCalled();
    });

    it("shows a done message when the send completes", () => {
        const container = render(
            <CreateCloudTeamCollectionBody
                {...baseProps({ loginState: signedIn, sendState: "done" })}
            />,
        );

        expect(
            container.querySelector('[data-testid="cloud-create-done-step"]'),
        ).not.toBeNull();
    });
});
