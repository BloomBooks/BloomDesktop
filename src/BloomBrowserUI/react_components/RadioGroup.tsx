import * as React from "react";
import {
    FormControlLabel,
    FormControl,
    RadioGroup as MuiRadioGroup,
    Radio
} from "@material-ui/core";

// This is a "controlled component".

/* Example use:
    const [method, setMethod] = BloomApi.useApiString("publish/android/method", "wifi");
    return(
        <ConciseRadioGroup
          value={method}
          setter={setMethod}
          choices={{
            wifi: "Share over Wi-FI",
            file: "Save to a file",
            usb: "Send via USB Cable"
          }}
        />)
*/

export const RadioGroup: React.FunctionComponent<{
    // the choices object should have an entry for each choice; the field of each is the key, and the value is the string
    choices: object;
    // the current value, must match one of the keys found in `choices`.
    value: string;
    onChange: (method: string) => void;
}> = props => {
    return (
        //this margin='dense' doesn't seem to do anything?
        <FormControl margin="dense">
            <MuiRadioGroup
                value={props.value}
                onChange={(event, newValue) => props.onChange(newValue)}
            >
                {Object.keys(props.choices).map(key => (
                    <FormControlLabel
                        key={key}
                        value={key}
                        control={<Radio color="primary" />}
                        label={(props.choices as any)[key]}
                        onChange={(e, n) => props.onChange(key)}
                    />
                ))}
            </MuiRadioGroup>
        </FormControl>
    );
};
