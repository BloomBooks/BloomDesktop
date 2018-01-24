import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10n";

export interface IRadioProps extends ILocalizationProps {
    wrapClassName: string;
    inputClassName: string;
    labelClassName: string;
    group: string; // name property of input, groups radios which switch together
    value: string; // identifies this radio in set
    groupValue: string; // current value of group; this one is checked if it has the group value.
    change: (string) => void;
}

// An radio button that is localizable and automatically handles unselecting others with the same group.
export class Radio extends LocalizableElement<IRadioProps, {}> {
    constructor(props) {
        super(props);

        // This binding is necessary to make `this` work in the callback
        this.selectRadio = this.selectRadio.bind(this);
    }
    render() {
        return (
            <div className={this.props.wrapClassName}>
                <input type="radio" className={this.props.inputClassName} name={this.props.group} value={this.props.value}
                    onClick={this.selectRadio} checked={this.props.value === this.props.groupValue} />
                <div className={this.props.labelClassName}>
                    {this.getLocalizedContent()}
                </div>
            </div>
        );
    }

    selectRadio() {
        // $("input[name='" + this.props.group + "']").prop("checked", false); // turn all off.
        // $("input[name='" + this.props.group + "' value='" + this.props.value + "']").prop("checked", true); // desired one on
        this.props.change(this.props.value);
    }
}