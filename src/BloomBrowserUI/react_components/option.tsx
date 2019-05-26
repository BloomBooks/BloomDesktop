import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";

interface IOptionProps extends ILocalizationProps {
    className: string;
    value: string;
}

// An <option> element that is localizable.
// We normally use getLocalizedContent, which returns a span with a class that
// we use to indicate localization problems. However, option is not allowed
// to contain HTML markup, only text, so trying to put a span in it has weird
// results...we get [object Object] instead of anything sensible.
export default class Option extends LocalizableElement<IOptionProps, {}> {
    public render() {
        return (
            <option className={this.props.className} value={this.props.value}>
                {this.getPlainLocalizedContent()}
            </option>
        );
    }
}
