import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement, Label, ILabelProps } from "./l10n";

interface ICheckboxProps extends ILocalizationProps {
    name: string;
    checked: boolean;
    onCheckChanged: (boolean) => void;
    className?: string;
}

// A checkbox that is localizable.
export class Checkbox extends LocalizableElement<ICheckboxProps, {}> {
    render() {
        return (
            <div>
                <input type="checkbox" className={this.props.className} name={this.props.name}
                    checked={this.props.checked} onChange={(event) => this.props.onCheckChanged(event.target.checked)} />
                <Label l10nKey={this.props.l10nKey}>
                    {this.getLocalizedContent()}
                </Label>
            </div>
        );
    }
}
