import * as React from "react";
import { ILocalizationProps, LocalizableElement } from "./l10n";

interface ILinkProps extends ILocalizationProps {
    id?: string;
    href?: string;
    onClick?: any;
}

// A normal html anchor element that is localizable.
export class Link extends LocalizableElement<ILinkProps, {}> {
    public render() {
        return (
            <a
                id={"" + this.props.id}
                // href must be defined in order to maintain normal link UI
                // I tried to do like the 'id' attribute above, but it caused an error.
                href={this.props.href ? this.props.href : ""}
                onClick={this.props.onClick}
            >
                {this.getLocalizedContent()}
            </a>
        );
    }
}

export default Link;
