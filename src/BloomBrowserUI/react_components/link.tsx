import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, ILocalizationState, LocalizableElement } from "./l10n";

interface ComponentProps extends ILocalizationProps {
    id: string;
    href: string;
    onClick?: any;
}

// A normal html anchor element that is localizable.
export default class Link extends LocalizableElement<ComponentProps, {}> {
    render() {
        return (
            <a id={this.props.id} href={this.props.href} onClick={this.props.onClick}>
                {this.getLocalizedContent()}
            </a>
        );
    }
}
