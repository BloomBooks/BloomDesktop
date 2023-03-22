/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Link as MuiLink } from "@mui/material";
import { LinkBaseProps } from "@mui/material/Link";

import { ILocalizationProps, LocalizableElement } from "./l10nComponents";
import { kBloomDisabledText } from "../utils/colorUtils";

interface ILinkProps extends ILocalizationProps {
    id?: string;
    href?: string;
    onClick?: () => void; // overrides following any href.
    disabled?: boolean;
    openInExternalBrowser?: boolean;
    color?:
        | "initial"
        | "inherit"
        | "primary"
        | "secondary"
        | "textPrimary"
        | "textSecondary"
        | "error";
    children: React.ReactNode; // The link text
}

// A link element that is localizable.
// Note: if you find yourself looking for a way to make one of these with the text forced to
// upper case, you may be really wanting a BloomButton with variant={text}
export class Link extends LocalizableElement<ILinkProps, {}> {
    public render() {
        return (
            //prettier-ignore
            <MuiLink
                underline="hover"
                className={
                    this.props.className +
                    (this.props.disabled ? " disabled" : "")
                }
                id={"" + this.props.id}
                color={this.props.color}
                target={this.props.openInExternalBrowser ? "_blank" : undefined}
                // href must be defined in order to maintain normal link UI
                // I tried to do like the 'id' attribute above, but it caused an error.
                href={
                    (this.props.href && !this.props.disabled)
                        ? this.props.href
                        : ""
                }
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
            >
                {this.getLocalizedContent()}
            </MuiLink>
        );
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
                    <MuiLink underline="hover" {...this.props}>
                        {parts.text.substring(idxOpen + 1, idxClose)}
                    </MuiLink>
                    {parts.text.substring(idxClose + 1)}
                </span>
            );
        }
        // We couldn't find the link text, return everything as a link.
        return (
            <span>
                <MuiLink underline="hover" {...this.props}>
                    {parts.text}
                </MuiLink>
            </span>
        );
    }
}

/**
 * The same as Link, but adds some default styling for making the link appear disabled.
 */
export const LinkWithDisabledStyles = (props: ILinkProps) => {
    return (
        // Note: The link text is in props.children which gets passed through the spread operator
        // The disabled status gets passed through props/spread operator too
        <Link
            {...props}
            css={css`
                &.disabled {
                    // color same as .MuiFormControlLabel-label.Mui-disabled
                    color: ${kBloomDisabledText};
                    cursor: default;

                    &:hover {
                        text-decoration: none;
                    }
                }
            `}
        ></Link>
    );
};
