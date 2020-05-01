import * as React from "react";
import TextField, { StandardTextFieldProps } from "@material-ui/core/TextField";
import { useState } from "react";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import "./RequestStringDialog.less";

interface IProps extends StandardTextFieldProps {
    l10nKeyForLabel: string;
}

// Displays a Text field with the label localized (only works if label is a string).
export const L10nTextField: React.FunctionComponent<IProps> = props => {
    // Do NOT initialize from props.l10nKeyForLabel; we want it to be different from that the first time,
    // so asyncGetText runs once.
    const [prevL10nKey, setPrevL10nKey] = useState("");
    const [labelContent, setLabelContent] = useState(props.label);

    // We track preL10NKey to prevent a render loop resulting from
    // getting another call to this fuction after setLabelContent changes state.
    if (
        props.l10nKeyForLabel != prevL10nKey &&
        props.label &&
        typeof props.label === "string"
    ) {
        const stringLabel: string = props.label;
        setPrevL10nKey(props.l10nKeyForLabel);
        theOneLocalizationManager
            .asyncGetText(props.l10nKeyForLabel, stringLabel, undefined)
            .done(result => {
                setLabelContent(result);
            });
    }

    return <TextField {...props} label={labelContent} />;
};
