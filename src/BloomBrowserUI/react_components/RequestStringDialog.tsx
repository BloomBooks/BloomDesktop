import * as React from "react";
import * as ReactDOM from "react-dom";
import Button from "@material-ui/core/Button";
import TextField from "@material-ui/core/TextField";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogContentText from "@material-ui/core/DialogContentText";
import DialogTitle from "@material-ui/core/DialogTitle";
import { useState } from "react";
import { string } from "prop-types";
import { LocalizedString } from "./l10nComponents";
import BloomButton from "./bloomButton";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import "./RequestStringDialog.less";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../bloomMaterialUITheme";

let root: HTMLElement | undefined;

// Displays a simple dialog allowing the user to enter or edit a string, with OK and Cancel buttons.
export const RequestStringDialog: React.FunctionComponent<{
    initialContent: string;
    l10nTitleKey: string;
    title: string;
    l10nKey: string; // i18n ID corresponding to label
    label: string; // appears above the edit box, labels the string being edited
    setValue: (result: string) => void; // called if user clicks OK, passed the possibly edited string.
}> = props => {
    const [open, setOpen] = useState(true);
    const [content, setContent] = useState(props.initialContent);
    const [prevL10nKey, setPrevL10nKey] = useState("");
    const [labelContent, setLabelContent] = useState(props.label);
    const handleClose = () => {
        setOpen(false);
        document.body.removeChild(root!);
    };
    const handleOk = () => {
        handleClose();
        props.setValue(content);
    };

    if (props.l10nKey != prevL10nKey) {
        setPrevL10nKey(props.l10nKey);
        theOneLocalizationManager
            .asyncGetText(props.l10nKey, props.label, undefined)
            .done(result => {
                setLabelContent(result);
            });
    }

    return (
        <ThemeProvider theme={theme}>
            <Dialog
                className="requestStringDialog"
                open={open}
                onClose={handleClose}
                aria-labelledby="form-dialog-title"
            >
                <DialogTitle id="form-dialog-title">
                    <LocalizedString
                        l10nKey={props.l10nTitleKey}
                        l10nComment={props.title}
                    >
                        Edit
                    </LocalizedString>
                </DialogTitle>
                <DialogContent>
                    <TextField
                        autoFocus={true}
                        margin="dense"
                        id="name"
                        label={labelContent}
                        fullWidth={true}
                        defaultValue={props.initialContent}
                        onChange={event => setContent(event.target.value)}
                    />
                </DialogContent>
                <DialogActions>
                    <BloomButton
                        id="okButton"
                        enabled={true}
                        l10nKey="Common.OK"
                        hasText={true}
                        size="medium"
                        onClick={handleOk}
                    >
                        OK
                    </BloomButton>
                    <BloomButton
                        enabled={true}
                        variant="outlined"
                        l10nKey="Common.Cancel"
                        hasText={true}
                        size="medium"
                        onClick={handleClose}
                    >
                        Cancel
                    </BloomButton>
                </DialogActions>
            </Dialog>
        </ThemeProvider>
    );
};

export function showRequestStringDialog(
    initialContent: string,
    l10nTitleKey: string,
    title: string,
    l10nKey: string,
    label: string,
    saveNewContent: (result: string) => void
) {
    root = document.createElement("div");
    document.body.appendChild(root);
    ReactDOM.render(
        <RequestStringDialog
            initialContent={initialContent}
            setValue={saveNewContent}
            l10nTitleKey={l10nTitleKey}
            title={title}
            l10nKey={l10nKey}
            label={label}
        />,
        root
    );
}
