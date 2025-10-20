/**
 * Test helpers for registration component tests
 */

import * as React from "react";
import {
    RegistrationContents,
    RegistrationInfo,
} from "../registrationContents";

/**
 * A wrapper component that maintains state for testing RegistrationContents.
 * This allows field values to persist when tests fill them in.
 */
export const StatefulRegistrationContents: React.FunctionComponent<{
    initialInfo: RegistrationInfo;
    emailRequiredForTeamCollection?: boolean;
    registrationIsOptional?: boolean;
    showOptOut?: boolean;
    mayChangeEmail?: boolean;
    onSubmit?: (info: RegistrationInfo) => void;
    onOptOut?: (info: RegistrationInfo) => void;
}> = (props) => {
    const [info, setInfo] = React.useState(props.initialInfo);
    const [showOptOutButton, setShowOptOutButton] = React.useState(false);

    // Simulate the 10-second delay for showing the opt-out button
    // If props.showOptOut is explicitly provided, use that value directly.
    // Otherwise, simulate the real RegistrationDialog behavior: start with false, then true after 10 seconds.
    React.useEffect(() => {
        if (props.showOptOut !== undefined) {
            // Explicitly controlled by props
            setShowOptOutButton(props.showOptOut);
            return;
        }

        // Simulate the real RegistrationDialog: start hidden, show after 10 seconds
        setShowOptOutButton(false);
        const timer = setTimeout(() => {
            setShowOptOutButton(true);
        }, 10000);

        return () => clearTimeout(timer);
    }, [props.showOptOut]);

    const handleInfoChange = (changes: Partial<RegistrationInfo>) => {
        setInfo((prev) => ({ ...prev, ...changes }));
    };

    const handleSubmit = (updatedInfo: RegistrationInfo) => {
        props.onSubmit?.(updatedInfo);
    };

    const handleOptOut = (updatedInfo: RegistrationInfo) => {
        props.onOptOut?.(updatedInfo);
    };

    return (
        <RegistrationContents
            info={info}
            onInfoChange={handleInfoChange}
            mayChangeEmail={props.mayChangeEmail ?? true}
            emailRequiredForTeamCollection={
                props.emailRequiredForTeamCollection ?? false
            }
            registrationIsOptional={props.registrationIsOptional ?? true}
            showOptOut={showOptOutButton}
            onSubmit={handleSubmit}
            onOptOut={handleOptOut}
        />
    );
};
