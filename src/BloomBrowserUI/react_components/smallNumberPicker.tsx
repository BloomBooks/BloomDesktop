import { css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import TextField from "@mui/material/TextField";
import ReactToolTip from "react-tooltip";

export interface INumberChooserProps {
    maxLimit: number; // a valid result cannot be greater than this
    minLimit?: number; // a valid result cannot be less than this
    handleChange: (newNumber: number) => void;
    onValidityChange?: (isValid: boolean) => void; // Notifies parent about validity changes
    tooltip?: string; // caller should localize
}

/**
 * A React component for selecting nonnegative integers within a specified range.
 * Ensures that the input adheres to the constraints and provides immediate feedback for invalid input.
 */
export const SmallNumberPicker: React.FunctionComponent<INumberChooserProps> = (
    props: INumberChooserProps,
) => {
    const minimumValue = props.minLimit ?? 0;
    const initialValue = minimumValue;
    const [displayValue, setDisplayValue] = useState(initialValue.toString());
    const [lastValidValue, setLastValidValue] = useState(initialValue);
    const [isInputValid, setIsInputValid] = useState(true);

    useEffect(() => {
        // Notify parent about initial validity
        props.onValidityChange?.(isInputValid);
    }, [isInputValid]);

    // We have the input allow empty string so that the user can clear the input before entering a new number
    // but don't persist or submit it
    function isValid(input: HTMLInputElement): boolean {
        return input.validity.valid && input.value !== "";
    }

    const handleNumberChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const newString = event.target.value;
        const newNum = event.target.valueAsNumber;

        // Don't allow typing in invalid characters; immediately snap back
        // Except we don't prevent underflow immediately, so e.g. users can type digits "10" when the minimum is 2.
        // Number inputs allow e for exponential notation but for a small number picker it only makes behavior more confusing
        if (
            event.target.validity.badInput ||
            event.target.validity.rangeOverflow ||
            newString.toLowerCase().includes("e")
        ) {
            return;
        }

        setDisplayValue(newString);

        const valid = isValid(event.target);
        setIsInputValid(valid);
        props.onValidityChange?.(valid);

        if (valid) {
            setLastValidValue(newNum);
            props.handleChange(newNum);
        }
    };

    // If the user clicks away with the input empty or invalid, restore the last valid value
    const handleBlur = (event: React.FocusEvent<HTMLInputElement>) => {
        const input = event.target;
        if (!isValid(input)) {
            setDisplayValue(lastValidValue.toString());
            setIsInputValid(true);
            props.onValidityChange?.(true);
        }
    };

    return (
        <div className="smallNumberPicker">
            <div data-tip={props.tooltip}>
                <TextField
                    css={css`
                        /* Don't display the little up/down arrows for number input */
                        input[type="number"] {
                            -moz-appearance: textfield;
                        }
                        input[type="number"]::-webkit-outer-spin-button,
                        input[type="number"]::-webkit-inner-spin-button {
                            -webkit-appearance: none;
                            margin: 0;
                        }

                        input[type="number"] {
                            text-align: right;
                        }
                    `}
                    onBlur={handleBlur}
                    onChange={handleNumberChange}
                    value={displayValue}
                    type="number"
                    inputProps={{
                        min: minimumValue,
                        max: props.maxLimit,
                        step: 1,
                    }}
                    variant="standard"
                />
            </div>
            <ReactToolTip place="left" effect="solid" />
        </div>
    );
};

export default SmallNumberPicker;
