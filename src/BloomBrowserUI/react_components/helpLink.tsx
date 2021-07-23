/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";
import Link from "@material-ui/core/Link";
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
                href={"/bloom/api/help/" + this.props.helpId}
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
    helpId: string;
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
                onClick={() => {}}
                css={css`
                    position: absolute !important;
                    right: 0;
                    top: 0;
                `}
                href={"/bloom/api/help/" + props.helpId}
            >
                What's This?
            </BloomButton>
        </div>
    );
};
