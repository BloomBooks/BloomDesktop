import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement,
} from "./l10nComponents";
import { Link as MuiLink } from "@mui/material";
import { splitAtLinkText } from "../utils/textUtils";

export interface ILocalizationPropsWithLink extends ILocalizationProps {
    href: string;
}

export class PWithLink extends LocalizableElement<
    ILocalizationPropsWithLink,
    ILocalizationState
> {
    public render() {
        const isLinkExternal = this.props.href.startsWith("https://");

        // Text within [] is for the link.
        const parts = this.getLocalizedContentAndClass();
        const split = splitAtLinkText(parts.text);
        if (split.found) {
            // We found the link text, piece together the desired output
            return (
                <p className={this.getClassName()}>
                    <span className={parts.l10nClass}>
                        {split.beforeLink}
                        <MuiLink
                            href={this.props.href}
                            target={isLinkExternal ? "_blank" : undefined}
                            rel="noreferrer"
                        >
                            {split.linkText}
                        </MuiLink>
                        {split.afterLink}
                    </span>
                </p>
            );
        }
        // We couldn't find the link text, return everything as a link.
        return (
            <p className={this.getClassName()}>
                <span className={parts.l10nClass}>
                    <a
                        href={this.props.href}
                        target={isLinkExternal ? "_blank" : undefined}
                        rel="noreferrer"
                    >
                        {parts.text}
                    </a>
                </span>
            </p>
        );
    }
}
