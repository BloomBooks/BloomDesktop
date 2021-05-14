import * as React from "react";
import * as MUI from "@material-ui/core";
import { LinkBaseProps } from "@material-ui/core/Link";

import { ILocalizationProps, LocalizableElement } from "./l10nComponents";

interface ILinkProps extends ILocalizationProps {
    id?: string;
    href?: string;
    onClick?: any; // overrides following any href.
    disabled?: boolean;
    color?:
        | "initial"
        | "inherit"
        | "primary"
        | "secondary"
        | "textPrimary"
        | "textSecondary"
        | "error";
}

// A link element that is localizable.
// Note: if you find yourself looking for a way to make one of these with the text forced to
// upper case, you may be really wanting a BloomButton with variant={text}
export class Link extends LocalizableElement<ILinkProps, {}> {
    public render() {
        // prettier-ignore
        return (<MUI.Link
                className={this.props.className + (this.props.disabled ? " disabled" : "")}
                id={"" + this.props.id}
                color={this.props.color}
                // href must be defined in order to maintain normal link UI
                // I tried to do like the 'id' attribute above, but it caused an error.
                href={(this.props.href && !this.props.disabled) ? this.props.href : ""}

                onClick={e => {
                    if (this.props.onClick) {
                        // If we have an onClick we don't expect to also have an href.
                        // In which case the code above will provide an empty href.
                        // The default behaviour will navigate to the top of the page,
                        // causing the whole react page to reload. So prevent that default.
                        e.preventDefault();
                        if (!this.props.disabled) {
                            this.props.onClick();
                        }
                    }
                }}
            >{this.getLocalizedContent()}</MUI.Link>);
    }
}

// Usage <TextWithEmbeddedLink l10nKey="blah" href="google.com"/>Click [here] or else</TextWithEmbeddedLink>
export class TextWithEmbeddedLink extends LocalizableElement<
    ILocalizationProps & LinkBaseProps,
    {}
> {
    public render() {
        // Text within [] is for the link.
        const parts = this.getLocalizedContentAndClass();
        const idxOpen = parts.text.indexOf("[");
        const idxClose = parts.text.indexOf("]", idxOpen + 1);
        if (idxOpen >= 0 && idxClose > idxOpen) {
            // We found the link text, piece together the desired output
            return (
                <span className={parts.l10nClass}>
                    {parts.text.substring(0, idxOpen)}
                    <MUI.Link {...this.props}>
                        {parts.text.substring(idxOpen + 1, idxClose)}
                    </MUI.Link>
                    {parts.text.substring(idxClose + 1)}
                </span>
            );
        }
        // We couldn't find the link text, return everything as a link.
        return (
            <span>
                <MUI.Link {...this.props}>{parts.text}</MUI.Link>
            </span>
        );
    }
}
