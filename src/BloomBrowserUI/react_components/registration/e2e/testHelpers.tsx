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
    mayChangeEmail?: boolean;
    onSubmit?: (info: RegistrationInfo) => void;
    onOptOut?: (info: RegistrationInfo) => void;
}> = (props) => {
    const [info, setInfo] = React.useState(props.initialInfo);

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
            onSubmit={handleSubmit}
            onOptOut={handleOptOut}
        />
    );
};
