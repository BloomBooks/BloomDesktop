/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import {
    ILocalizationProps,
    LocalizableElement,
    Label
} from "./l10nComponents";

interface ICheckboxProps extends ILocalizationProps {
    id?: string;
    name?: string;
    inputClassName?: string;
    disabled?: boolean;
    className?: string;
    // toggle between on, off, and unknown
    tristate?: boolean;
    // Note: checked can be undefined, if tristate==true
    checked?: boolean;
    // Note: the parameter given will be undefined if  tristate==true and the checkbox is in the indeterminate state
    onCheckChanged?: (boolean) => void;
}

// A checkbox that is localizable and can toggle between either a 2 or 3 states.
export class Checkbox extends LocalizableElement<ICheckboxProps, {}> {
    // Resist the temptation to change null to undefined here.
    // This type has to match the 'ref' attribute below, which has "| null".
    private input: HTMLInputElement | null;
    private previousTriState: boolean | undefined;
    public constructor(props: ICheckboxProps) {
        super(props);
        this.previousTriState = props.checked;
    }

    public componentDidMount() {
        // set the initial "unknown" state
        if (this.props.tristate && this.input) {
            this.input.indeterminate = this.props.checked === undefined;
        }
    }

    private onLabelClicked() {
        // We expect the effect of clicking the label will be to set the check to the
        // opposite state, so that's what we pass. (But whether it really changes is
        // up to the owner changing the prop value. So it won't have happened yet.)
        if (this.input && !this.props.disabled) {
            if (!this.props.tristate) {
                this.input.checked = !this.input.checked;
            }
            this.onChange(this.input);
        }
    }

    public render() {
        return (
            <div
                // To allow emotion css to apply to the Checkbox class, it's important that our main
                // div is given the className of the Checkbox. (An earlier version of this code
                // had a property wrapClassName for this, and passed className to the input
                // element. This produced bizarre results with emotion. If you really need to apply
                // a class to the input element from outside, it's probably best done with emotion
                // using a child element rule. If you must pass in a className for that element,
                // props.inputClassName is available.)
                className={this.props.className}
                css={css`
                    display: flex;
                    align-items: baseline;
                `}
            >
                <input
                    id={this.props.id}
                    type="checkbox"
                    className={this.props.inputClassName}
                    name={this.props.name}
                    disabled={this.props.disabled}
                    checked={this.props.checked}
                    onChange={event => {
                        this.onChange(event.target);
                    }}
                    ref={input => (this.input = input)}
                    css={css`
                        margin-right: 10px;
                    `}
                />
                <Label
                    {...this.props}
                    onClick={() => this.onLabelClicked()}
                    className={this.props.disabled ? "disabled" : ""}
                >
                    {/* this.props.children is the English text */}
                    {this.props.children}
                </Label>
            </div>
        );
    }

    private onChange(target: HTMLInputElement) {
        if (!this.props.tristate) {
            if (this.props.onCheckChanged) {
                this.props.onCheckChanged(target.checked);
            }
        } else {
            // we're in on/off/unknown mode, so handle the progress ourselves
            switch (this.previousTriState) {
                case undefined:
                    this.previousTriState = target.checked = true;
                    target.indeterminate = false;
                    break;
                case true:
                    this.previousTriState = target.checked = false;
                    target.indeterminate = false;
                    break;
                case false:
                    target.indeterminate = true;
                    this.previousTriState = undefined;
                    break;
            }
            if (this.props.onCheckChanged) {
                this.props.onCheckChanged(this.previousTriState);
            }
        }
    }
}
