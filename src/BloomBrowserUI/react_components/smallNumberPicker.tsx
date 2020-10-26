import * as React from "react";
import { useState } from "react";
import TextField from "@material-ui/core/TextField";
import Tooltip from "@material-ui/core/Tooltip";
import { makeStyles } from "@material-ui/core/styles";
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
    // The small trick below is that minLimit could be defined as zero... but that's falsey.
    const initialValue = props.minLimit
        ? props.minLimit
        : props.minLimit === undefined
        ? 1
        : 0;
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

    const useStylesNumberPicker = makeStyles(theme => ({
        arrow: {
            color: theme.palette.common.black
        },
        tooltip: {
            backgroundColor: theme.palette.common.black
        }
    }));

    const NumberPickerTooltip = props => {
        const classes = useStylesNumberPicker();

        return (
            <Tooltip
                arrow={true}
                classes={classes}
                placement="left"
                {...props}
            />
        );
    };

    return (
        <div className="smallNumberPicker">
            {/* <NumberPickerTooltip title={props.tooltip}> */}
            <TextField onChange={handleNumberChange} value={chosenNumber} />
            {/* </NumberPickerTooltip> */}
        </div>
    );
};

export default SmallNumberPicker;
