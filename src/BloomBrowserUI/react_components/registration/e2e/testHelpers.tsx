/**
 * Test helpers for registration component tests
 */

import * as React from "react";
import {
    RegistrationContents,
    RegistrationInfo,
} from "../registrationContents";

/**
 * A wrapper component for testing RegistrationContents.
 * Now that RegistrationContents manages its own state, this is just a simple wrapper.
 */
export const StatefulRegistrationContents: React.FunctionComponent<{
    initialInfo: RegistrationInfo;
    emailRequiredForTeamCollection?: boolean;
    mayChangeEmail?: boolean;
    onSubmit?: (info: RegistrationInfo) => void;
}> = (props) => {
    const handleSubmit = (updatedInfo: RegistrationInfo) => {
        props.onSubmit?.(updatedInfo);
    };

    return (
        <RegistrationContents
            initialInfo={props.initialInfo}
            mayChangeEmail={props.mayChangeEmail ?? true}
            emailRequiredForTeamCollection={
                props.emailRequiredForTeamCollection ?? false
            }
            onSubmit={handleSubmit}
        />
    );
};
