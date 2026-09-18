import * as React from "react";
import { useCallback } from "react";
import { getToolboxBundleExports } from "../../../js/workspaceFrames";
import $ from "jquery";
import { css, SerializedStyles } from "@emotion/react";
import { kBloomBlue } from "../../../../utils/colorUtils";

export const ReaderDialogTextarea: React.FunctionComponent<{
    /**
     * Called with the box's new text. Named for what it does rather than what the callers do
     * with it: they all happen to write settings, but this component knows nothing about
     * settings, and the shared updateSettings helper is a different thing entirely.
     */
    onValueChange: (value: string) => void;
    value: string;
    extraStyles: SerializedStyles;
    /** Accessible name for the box; it is labelled only by a nearby heading, not a <label>. */
    ariaLabel: string;
    /**
     * A stable hook for the e2e suite. The aria-label above is localized, so it cannot serve
     * as one.
     */
    testId?: string;
}> = (props) => {
    const activateLongPress = useCallback(
        (textarea: HTMLTextAreaElement | null) => {
            if (textarea) {
                getToolboxBundleExports()!.activateLongPressFor($(textarea));
            }
        },
        [],
    );
    return (
        <textarea
            ref={activateLongPress}
            aria-label={props.ariaLabel}
            data-testid={props.testId}
            value={props.value}
            onChange={(event) => props.onValueChange(event.target.value)}
            onBlur={(event) => props.onValueChange(event.currentTarget.value)}
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
