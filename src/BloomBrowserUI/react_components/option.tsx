import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, ILocalizationState, LocalizableElement } from "./l10n";

interface ComponentProps extends ILocalizationProps {
    className: string;
    value: string;
}

// An <option> element that is localizable.
export default class Option extends LocalizableElement<ComponentProps, {}> {
    render() {
        return (
            <option className={this.props.className} value={this.props.value}>
                {this.getLocalizedContent()}
            </option>
        );
    }
}
