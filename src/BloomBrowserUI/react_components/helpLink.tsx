import * as React from "react";
import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement,
} from "./l10nComponents";
import Link from "@mui/material/Link";

interface IHelpLinkProps extends ILocalizationProps {
    helpId: string;
    style?: React.CSSProperties;
}

// just an html anchor that knows how to localize and how turn a Bloom help id into a url
export class HelpLink extends LocalizableElement<
    IHelpLinkProps,
    ILocalizationState
> {
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
