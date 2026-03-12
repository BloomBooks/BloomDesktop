import * as React from "react";
import { useState } from "react";
import TextField from "@mui/material/TextField";
import ReactToolTip from "react-tooltip";
import "./smallNumberPicker.less";

export interface INumberChooserProps {
    maxLimit: number; // a valid result cannot be greater than this
    minLimit?: number; // a valid result cannot be less than this
    handleChange: (newNumber: number) => void;
    onValidityChange?: (isValid: boolean) => void;
    tooltip?: string; // caller should localize
}

/**
 * Small numeric input intended for choosing positive integers only.
 * Current behavior is digit-only entry with min/max validation.
 */
export const SmallNumberPicker: React.FunctionComponent<INumberChooserProps> = (
    props: INumberChooserProps,
) => {
    const initialValue = props.minLimit === undefined ? 1 : props.minLimit;
    const [displayValue, setDisplayValue] = useState(initialValue.toString());

    const isWithinLimits = (value: number): boolean => {
        if (value > props.maxLimit) {
            return false;
        }
        if (props.minLimit !== undefined && value < props.minLimit) {
            return false;
        }
        return true;
    };

    const handleNumberChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const newString = event.target.value;

        // Keep the field digit-only by ignoring any non-digit input.
        if (!/^\d*$/.test(newString)) {
            return;
        }

        if (newString === "") {
            setDisplayValue("");
            props.onValidityChange?.(false);
            return;
        }

        const newNum = parseInt(newString, 10);
        if (!Number.isNaN(newNum) && newNum > props.maxLimit) {
            return;
        }

        setDisplayValue(newString);

        if (!Number.isNaN(newNum) && isWithinLimits(newNum)) {
            props.handleChange(newNum);
            props.onValidityChange?.(true);
        } else {
            props.onValidityChange?.(false);
        }
    };

    // We would love to set the TextField "type" to "number", but this introduces up/down arrows that we
    // can't get rid of in Firefox and have the input still perform as a number input. This means we have to
    // use a "text" style input and handle max and min and letter input in code. Any invalid input sets the
    // input value back to the 'minLimit'.
    return (
        <div className="smallNumberPicker">
            <div data-tip={props.tooltip}>
                <TextField
                    onChange={handleNumberChange}
                    value={displayValue}
                    variant="standard"
                />
            </div>
            <ReactToolTip place="left" effect="solid" />
        </div>
    );
};

export default SmallNumberPicker;
