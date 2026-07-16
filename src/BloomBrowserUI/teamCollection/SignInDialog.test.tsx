import { act } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderTestRoot as render } from "../utils/testRender";
import { SignInDialogBody } from "./SignInDialog";
import { ISharingLoginState } from "./sharingApi";

// Tests the presentational SignInDialogBody directly with injected props/callbacks (no
// network layer), same approach as CreateCloudTeamCollectionBody's own tests. Covers both
// sign-in modes (task 06's `sharing/loginState` mode field): dev-mode's email/password form,
// and "cloud" mode's browser-sign-in button (task 12; Option A decided 8 Jul 2026).

const devMode: ISharingLoginState = { mode: "dev", signedIn: false };
const cloudMode: ISharingLoginState = { mode: "cloud", signedIn: false };

function baseProps(
    overrides: Partial<React.ComponentProps<typeof SignInDialogBody>> = {},
) {
    return {
        loginState: devMode,
        email: "",
        password: "",
        onEmailChange: vi.fn(),
        onPasswordChange: vi.fn(),
        onSignIn: vi.fn(),
        onOpenBrowserSignIn: vi.fn(),
        submitAttempts: 0,
        signInError: undefined,
        ...overrides,
    };
}

describe("SignInDialogBody", () => {
    it("dev mode: shows the email/password form, not the not-yet-available message", () => {
        const container = render(<SignInDialogBody {...baseProps()} />);

        expect(
            container.querySelector('[data-testid="signin-dev-form"]'),
        ).not.toBeNull();
        expect(
            container.querySelector('[data-testid="signin-not-available"]'),
        ).toBeNull();
        expect(
            container.querySelector('[data-testid="signin-email"]'),
        ).not.toBeNull();
        expect(
            container.querySelector('[data-testid="signin-password"]'),
        ).not.toBeNull();
    });

    it("dev mode: clicking Sign In calls onSignIn", () => {
        const onSignIn = vi.fn();
        const container = render(
            <SignInDialogBody {...baseProps({ onSignIn })} />,
        );

        const button = container.querySelector(
            '[data-testid="signin-button"]',
        ) as HTMLButtonElement;
        expect(button).not.toBeNull();
        act(() => button.click());
        expect(onSignIn).toHaveBeenCalled();
    });

    it("dev mode: shows the sign-in error when one is provided", () => {
        const container = render(
            <SignInDialogBody
                {...baseProps({ signInError: "Invalid credentials" })}
            />,
        );

        const error = container.querySelector('[data-testid="signin-error"]');
        expect(error).not.toBeNull();
        expect(error!.textContent).toContain("Invalid credentials");
    });

    it("cloud mode: shows the browser sign-in button, not the dev form or the not-yet-available message", () => {
        const container = render(
            <SignInDialogBody {...baseProps({ loginState: cloudMode })} />,
        );

        expect(
            container.querySelector('[data-testid="signin-cloud-browser"]'),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="signin-open-browser-button"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector('[data-testid="signin-dev-form"]'),
        ).toBeNull();
        expect(
            container.querySelector('[data-testid="signin-not-available"]'),
        ).toBeNull();
    });

    it("cloud mode: clicking Sign In calls onOpenBrowserSignIn", () => {
        const onOpenBrowserSignIn = vi.fn();
        const container = render(
            <SignInDialogBody
                {...baseProps({ loginState: cloudMode, onOpenBrowserSignIn })}
            />,
        );

        const button = container.querySelector(
            '[data-testid="signin-open-browser-button"]',
        ) as HTMLButtonElement;
        expect(button).not.toBeNull();
        act(() => button.click());
        expect(onOpenBrowserSignIn).toHaveBeenCalled();
    });
});
