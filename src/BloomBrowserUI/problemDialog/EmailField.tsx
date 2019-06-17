import * as React from "react";
import "./ProblemDialog.less";
import { TextField } from "@material-ui/core";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { useDebouncedCallback } from "use-debounce";
import { useDrawAttention } from "./UseDrawAttention";

//Note: the "isemail" package was not compatible with geckofx 45, so I'm just going with regex
// from https://stackoverflow.com/a/46181/723299
// NB: should handle emails like 用户@例子.广告
const emailPattern = /^(([^<>()\[\]\.,;:\s@\"]+(\.[^<>()\[\]\.,;:\s@\"]+)*)|(\".+\"))@(([^<>()[\]\.,;:\s@\"]+\.)+[^<>()[\]\.,;:\s@\"]{2,})$/i;

export function isValidEmail(email: string): boolean {
    return emailPattern.test(email);
}
export const EmailField: React.FunctionComponent<{
    submitAttempts: number;
    email: string;
    onChange: (email: string) => void;
}> = props => {
    //const [attentionClass, setAttentionClass] = useState("");

    const [emailValid, setEmailValid] = useState(false);

    const [debouncedEmailCheck] = useDebouncedCallback(value => {
        setEmailValid(isValidEmail(value));
    }, 100);

    // This is needed in order to get the initial check, when we are loading the stored email address from the api
    React.useEffect(() => {
        debouncedEmailCheck(props.email);
    }, [props.email]);

    const attentionClass = useDrawAttention(
        props.submitAttempts,
        () => emailValid
    );

    return (
        <TextField
            className={"email " + attentionClass}
            variant="outlined"
            label="Email"
            rows="1"
            InputLabelProps={{
                shrink: true
            }}
            multiline={false}
            aria-label="email"
            error={
                (props.email.length > 0 && !emailValid) ||
                (props.submitAttempts > 0 && !emailValid)
            }
            onChange={event => {
                props.onChange(event.target.value);
                //  debouncedEmailCheck(event.target.value);
            }}
            value={props.email}
        />
    );
};
