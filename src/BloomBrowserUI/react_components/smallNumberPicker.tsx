import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import TextField from "@mui/material/TextField";
import ReactToolTip from "react-tooltip";
import "./smallNumberPicker.less";

export interface INumberChooserProps {
    maxLimit: number; // a valid result cannot be greater than this
    minLimit?: number; // a valid result cannot be less than this
    handleChange: (newNumber: number) => void;
    tooltip?: string; // caller should localize
}

export const SmallNumberPicker: React.FunctionComponent<INumberChooserProps> = (
    props: INumberChooserProps,
) => {
    const initialValue = props.minLimit ?? 1;
    const [displayValue, setDisplayValue] = useState(initialValue.toString());
    const [lastValidValue, setLastValidValue] = useState(initialValue);

    const handleNumberChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const newString = event.target.value;
        const newNum = event.target.valueAsNumber;

        // Don't allow typing in invalid input; immediately snap back
        if (
            !event.target.validity.valid ||
            newString.toLowerCase().includes("e") // number inputs allow e for exponential notation but for a small number picker it only makes behavior more confusing
        ) {
            return;
        }

        setDisplayValue(newString);
        // We want to allow empty string so the user can clear the input before entering a new number
        if (newString === "") {
            return;
        }

        setLastValidValue(newNum);
        props.handleChange(newNum);
    };

    // If the user clicks away with the input empty, restore the last valid value
    const handleBlur = (event: React.FocusEvent<HTMLInputElement>) => {
        const input = event.target;
        if (input.value === "") {
            setDisplayValue(lastValidValue.toString());
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
                    `}
                    onBlur={handleBlur}
                    onChange={handleNumberChange}
                    value={displayValue}
                    type="number"
                    inputProps={{
                        min: props.minLimit,
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
