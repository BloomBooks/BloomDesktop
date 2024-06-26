/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";
import Link from "@mui/material/Link";
import BloomButton from "./bloomButton";

interface IHelpLinkProps extends ILocalizationProps {
    helpId: string;
    style?: React.CSSProperties;
}

// just an html anchor that knows how to localize and how turn a Bloom help id into a url
export class HelpLink extends LocalizableElement<IHelpLinkProps, {}> {
    public render() {
        return (
            <Link
                style={this.props.style}
                href={"/bloom/api/help?topic=" + this.props.helpId}
                underline="hover"
            >
                {this.getLocalizedContent()}
            </Link>
        );
    }
}

export default HelpLink;

// A common help link is just called "help" and that has a high zindex so that it is usable even when the tool is disabled by an overlay
export class ToolBottomHelpLink extends React.Component<{ helpId: string }> {
    public render() {
        return (
            <HelpLink
                style={{ zIndex: 100 }}
                l10nKey="Common.Help"
                helpId={this.props.helpId}
            >
                Help
            </HelpLink>
        );
    }
}

// puts a "What's this?" in the upper right hand corner of the block
export const WhatsThisBlock: React.FunctionComponent<{
    // we could add this when we are using this for built-in help:   helpId: string;
    url: string;
}> = props => {
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
