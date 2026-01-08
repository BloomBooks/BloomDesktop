import {
    ILocalizationProps,
    ILocalizationState,
    LocalizableElement,
} from "./l10nComponents";
import { Link as MuiLink } from "@mui/material";

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
        const idxOpen = parts.text.indexOf("[");
        const idxClose = parts.text.indexOf("]", idxOpen + 1);
        if (idxOpen >= 0 && idxClose > idxOpen) {
            // We found the link text, piece together the desired output
            return (
                <p className={this.getClassName()}>
                    <span className={parts.l10nClass}>
                        {parts.text.substring(0, idxOpen)}
                        <MuiLink
                            href={this.props.href}
                            target={isLinkExternal ? "_blank" : undefined}
                            rel="noreferrer"
                        >
                            {parts.text.substring(idxOpen + 1, idxClose)}
                        </MuiLink>
                        {parts.text.substring(idxClose + 1)}
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
