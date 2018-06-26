import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10n";

interface IOptionProps extends ILocalizationProps {
    className: string;
    value: string;
}

// An <option> element that is localizable.
export default class Option extends LocalizableElement<IOptionProps, {}> {
    render() {
        return (
            <option className={this.props.className} value={this.props.value}>
                {this.getLocalizedContent()}
            </option>
        );
    }
}
