// This file is used by `yarn scope` to open the component in a browser. It does so in a way that works with skills/scope/skill.md.

import * as React from "react";
import { css } from "@emotion/react";
import { RegistrationContents } from "./registrationContents";
import { RegistrationInfo } from "./registrationTypes";

type RegistrationHarnessProps = {
    initialInfo: RegistrationInfo;
    mayChangeEmail: boolean;
    emailRequiredForTeamCollection: boolean;
};

const RegistrationHarness: React.FC<RegistrationHarnessProps> = (props) => {
    const [lastCloseResult, setLastCloseResult] = React.useState<
        string | undefined
    >();

    return (
        <div
            css={css`
                font-family: sans-serif;
                background: #f5f5f5;
                padding: 24px;
                min-height: 100vh;
                display: flex;
                flex-direction: column;
                align-items: flex-start;
                gap: 12px;
            `}
        >
            <div
                css={css`
                    color: #333;
                    font-size: 13px;
                `}
            >
                Close result: {lastCloseResult ?? "(none yet)"}
            </div>
            <div
                css={css`
                    background: white;
                    padding: 16px;
                    border-radius: 6px;
                    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);
                `}
            >
                <RegistrationContents
                    initialInfo={props.initialInfo}
                    mayChangeEmail={props.mayChangeEmail}
                    emailRequiredForTeamCollection={
                        props.emailRequiredForTeamCollection
                    }
                    optOutDelaySeconds={2}
                    onClose={(success) =>
                        setLastCloseResult(
                            success ? "onClose(true)" : "onClose(false)",
                        )
                    }
                />
            </div>
        </div>
    );
};

export const withExistingInfo: React.FC = () => {
    return (
        <RegistrationHarness
            initialInfo={{
                firstName: "John",
                surname: "Smith",
                email: "john.smith@example.com",
                organization: "Test Organization",
                usingFor: "Testing purposes",
                hadEmailAlready: true,
            }}
            mayChangeEmail={false}
            emailRequiredForTeamCollection={false}
        />
    );
};

export const emailRequired: React.FC = () => {
    return (
        <RegistrationHarness
            initialInfo={{
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            }}
            mayChangeEmail={true}
            emailRequiredForTeamCollection={true}
        />
    );
};

export const blank: React.FC = () => {
    return (
        <RegistrationHarness
            initialInfo={{
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            }}
            mayChangeEmail={true}
            emailRequiredForTeamCollection={false}
        />
    );
};

export default blank;
