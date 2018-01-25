import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10n";

export interface IRadioProps extends ILocalizationProps {
    wrapClass: string; // class for a div that wraps the input and label
    inputClass: string; // class for the input element (the radio button itself)
    labelClass: string; // class for the label (text next to the radio button)
    group: string; // name property of input, groups radios which switch together
    value: string; // identifies this radio in set
    groupValue: string; // current value of group; this one is checked if it has the group value.
    onSelected: (string) => void; // passed this button's value when it is clicked.
}

// A radio button that is localizable. Typically, groupValue is part of the state of the parent,
// and change sets it. React rendering then automatically turns off all but the selected button.
// For example, three radio buttons might each have group="color" and suitable styles.
// One could have value="red", another value="blue", another value="green".
// All would have groupValue={this.state.color} and change="val=>this.setState({color: val})"
export class Radio extends LocalizableElement<IRadioProps, {}> {
    constructor(props) {
        super(props);
    }
    render() {
        return (
            <div className={this.props.wrapClass}>
                <input type="radio" className={this.props.inputClass} name={this.props.group} value={this.props.value}
                    onClick={() => this.props.onSelected(this.props.value)} checked={this.props.value === this.props.groupValue} />
                <div className={this.props.labelClass}>
                    {this.getLocalizedContent()}
                </div>
            </div>
        );
    }
}