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
    onClose?: () => void;
}
