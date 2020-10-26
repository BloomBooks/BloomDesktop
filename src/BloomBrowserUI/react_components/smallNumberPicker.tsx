import * as React from "react";
import { useState } from "react";
import TextField from "@material-ui/core/TextField";
import ReactToolTip from "react-tooltip";
import "./smallNumberPicker.less";

export interface INumberChooserProps {
    maxLimit: number; // a valid result cannot be greater than this
    minLimit?: number; // a valid result cannot be less than this
    handleChange: (newNumber: number) => void;
    tooltip?: string; // caller should localize
}

export const SmallNumberPicker: React.FunctionComponent<INumberChooserProps> = (
    props: INumberChooserProps
) => {
    const initialValue = props.minLimit === undefined ? 1 : props.minLimit;
    const [chosenNumber, setChosenNumber] = useState(initialValue);

    const handleNumberChange = (event: any) => {
        const newString = event.target.value;
        const newNum = parseInt(newString);
        if (
            !newNum ||
            newNum > props.maxLimit ||
            (props.minLimit && newNum < props.minLimit)
        ) {
            setChosenNumber(initialValue);
            props.handleChange(initialValue);
        } else {
            setChosenNumber(newNum);
            props.handleChange(newNum);
        }
    };

    // We would love to set the TextField "type" to "number", but this introduces up/down arrows that we
    // can't get rid of in Firefox and have the input still perform as a number input. This means we have to
    // use a "text" style input and handle max and min and letter input in code. Any invalid input sets the
    // input value back to the 'minLimit'.
    return (
        <div className="smallNumberPicker">
            <div data-tip={props.tooltip}>
                <TextField onChange={handleNumberChange} value={chosenNumber} />
            </div>
            <ReactToolTip place="left" effect="solid" />
        </div>
    );
};

export default SmallNumberPicker;
