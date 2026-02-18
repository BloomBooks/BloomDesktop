import * as React from "react";
import { css } from "@emotion/react";
import { getString } from "../utils/bloomApi";

export const EditTabHost: React.FunctionComponent = () => {
    const [frameUrl, setFrameUrl] = React.useState("");

    // Poll for the edit frame URL until the edit host has initialized enough to provide it.
    React.useEffect(() => {
        let isActive = true;
        let intervalId: number;

        const tryGetFrameUrl = () => {
            getString("workspace/editFrameUrl", (value) => {
                if (!isActive || !value) {
                    return;
                }

                setFrameUrl(value);
                window.clearInterval(intervalId);
            });
        };

        tryGetFrameUrl();
        intervalId = window.setInterval(tryGetFrameUrl, 400);

        return () => {
            isActive = false;
            window.clearInterval(intervalId);
        };
    }, []);

    if (!frameUrl) {
        return (
            <div
                css={css`
                    display: flex;
                    width: 100%;
                    height: 100%;
                    align-items: center;
                    justify-content: center;
                    color: white;
                    font-size: 14px;
                `}
            >
                Loading Edit tab...
            </div>
        );
    }

    return (
        <iframe
            title="EditTabHost"
            src={frameUrl}
            css={css`
                width: 100%;
                height: 100%;
                border: none;
                display: block;
            `}
        />
    );
};
