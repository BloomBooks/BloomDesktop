/**
 * Types and constants for the registration component.
 * This file contains NO JSX, so it can be safely imported by Playwright tests.
 */

export const kInactivitySecondsBeforeShowingOptOut = 10;

export interface RegistrationInfo {
    firstName: string;
    surname: string;
    email: string;
    organization: string;
    usingFor: string;
    hadEmailAlready: boolean;
}

export interface IRegistrationContentsProps {
    initialInfo: RegistrationInfo;
    mayChangeEmail?: boolean;
    emailRequiredForTeamCollection?: boolean;
    onClose?: (hasValidEmail: boolean) => void;
    /** Override the delay (in seconds) before showing the opt-out button. Defaults to kInactivitySecondsBeforeShowingOptOut. */
    optOutDelaySeconds?: number;
    /**
     * For cloud Team Collections, registration identity *is* the signed-in Bloom account: pass
     * the account's (verified) email here to pre-fill and lock the email field to it, regardless
     * of mayChangeEmail. Unlike the folder-TC "Check in to change email" lock (which just means
     * "already registered"), this lock means the email can never diverge from the account you're
     * signed into for this cloud collection.
     */
    cloudAccountEmail?: string;
}
