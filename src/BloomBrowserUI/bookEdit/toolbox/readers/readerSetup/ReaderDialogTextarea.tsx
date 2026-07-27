import * as React from "react";
import { useCallback } from "react";
import { getToolboxBundleExports } from "../../../js/workspaceFrames";
import $ from "jquery";
import { css, SerializedStyles } from "@emotion/react";
import { kBloomBlue } from "../../../../utils/colorUtils";

export const ReaderDialogTextarea: React.FunctionComponent<{
    updateSettings: (value: string) => void;
    value: string;
    extraStyles: SerializedStyles;
}> = (props) => {
    const activateLongPressForSightWords = useCallback(
        (textarea: HTMLTextAreaElement | null) => {
            if (textarea) {
                getToolboxBundleExports()?.activateLongPressFor($(textarea));
            }
        },
        [],
    );
    return (
        <textarea
            ref={activateLongPressForSightWords}
            value={props.value}
            onChange={(event) => props.updateSettings(event.target.value)}
            onBlur={(event) => props.updateSettings(event.currentTarget.value)}
            css={css`
                box-sizing: border-box;
                resize: none;
                overflow: auto;
                border: 1px solid #d8dce0;
                border-radius: 6px;
                padding: 8px;
                color: #202020;
                font-size: 10pt;
                line-height: 17px;
                &:focus {
                    outline: none;
                    border: 2px solid ${kBloomBlue};
                    padding: 7px;
                }
                ${props.extraStyles}
            `}
        />
    );
};
