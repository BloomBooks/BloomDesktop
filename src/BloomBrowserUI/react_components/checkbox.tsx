import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement, Label, ILabelProps } from "./l10n";

interface ICheckboxProps extends ILocalizationProps {
    id?: string;
    name: string;
    checked: boolean;
    onCheckChanged: (boolean) => void;
    className?: string;
}

// A checkbox that is localizable.
export class Checkbox extends LocalizableElement<ICheckboxProps, {}> {
    input: HTMLInputElement;
    onLabelClicked() {
        // We expect the effect of clicking the label will be to set the check to the
        // opposite state, so that's what we pass. (But whether it really changes is
        // up to the owner changing the prop value. So it won't have happened yet.)
        this.props.onCheckChanged(!this.input.checked);
    }
    render() {
        return (
            <div>
                <input id={this.props.id} type="checkbox" className={this.props.className} name={this.props.name}
                    checked={this.props.checked} onChange={(event) => this.props.onCheckChanged(event.target.checked)}
                    ref={(input) => this.input = input} />
                <label onClick={() => this.onLabelClicked()}>
                    {this.getLocalizedContent()}
                </label>
            </div>
        );
    }
}
