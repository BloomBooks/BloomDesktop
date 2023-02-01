import * as React from "react";
import * as ReactDOM from "react-dom";
import { L10nTextField } from "./L10nTextField";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import { useState } from "react";
import { LocalizedString } from "./l10nComponents";
import BloomButton from "./bloomButton";
import "./RequestStringDialog.less";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { lightTheme } from "../bloomMaterialUITheme";

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
    const handleClose = () => {
        setOpen(false);
        document.body.removeChild(root!);
    };
    const handleOk = () => {
        handleClose();
        props.setValue(content);
    };

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
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
                        <L10nTextField
                            autoFocus={true}
                            margin="dense"
                            id="name"
                            l10nKeyForLabel={props.l10nKey}
                            label={props.label}
                            fullWidth={true}
                            variant="standard"
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
        </StyledEngineProvider>
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
