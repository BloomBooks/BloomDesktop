import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "./bloomButton";

// puts a "What's this?" in the upper right hand corner of the block
export const WhatsThisBlock: React.FunctionComponent<{
    // we could add this when we are using this for built-in help:   helpId: string;
    url: string;
}> = (props) => {
    return (
        <div
            css={css`
                // the absolute positioning of the button with be with respect to this
                position: relative;
            `}
            {...props}
        >
            {props.children}
            <BloomButton
                variant="text"
                enabled={true}
                l10nKey="Common.WhatsThis"
                css={css`
                    position: absolute !important;
                    right: 0;
                    top: 0;
                    // The MUI component that builds the actual button adds padding, which causes
                    // layout problems in some cases. So we reduce the vertical padding by 1 px top and bottom.
                    // This is enough to keep the BulkBloomPubDialog from having a vertical scrollbar.
                    // See BL-12249.
                    padding-top: 5px;
                    padding-bottom: 5px;
                `}
                href={props.url}
            >
                What's This?
            </BloomButton>
        </div>
    );
};
