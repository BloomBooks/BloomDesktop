import { css } from "@emotion/react";
import * as React from "react";
import { ProgressBar } from "../../react_components/Progress/ProgressBar";

export const InlineProgressStatus: React.FunctionComponent<{
    showProgress: boolean;
    progressLabel?: string;
    progressPercent: number;
}> = (props) => {
    if (!props.showProgress || !props.progressLabel) {
        return null;
    }

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                gap: 6px;
                margin: 12px 0 0 0;
                max-width: 900px;
            `}
        >
            <div
                css={css`
                    display: flex;
                    justify-content: space-between;
                    align-items: baseline;
                    gap: 12px;
                    font-weight: 600;
                `}
            >
                <span>{props.progressLabel}</span>
                {props.progressPercent > 0 && (
                    <span>{props.progressPercent}%</span>
                )}
            </div>
            {props.progressPercent > 0 && (
                <ProgressBar percentage={props.progressPercent} />
            )}
        </div>
    );
};
