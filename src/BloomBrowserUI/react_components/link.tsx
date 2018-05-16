import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10n";

interface ILinkProps extends ILocalizationProps {
    id: string;
    href: string;
    onClick?: any;
}

// A normal html anchor element that is localizable.
export class Link extends LocalizableElement<ILinkProps, {}> {
    public render() {
        return (
            <a id={this.props.id} href={this.props.href} onClick={this.props.onClick}>
                {this.getLocalizedContent()}
            </a>
        );
    }
}

export default Link;
