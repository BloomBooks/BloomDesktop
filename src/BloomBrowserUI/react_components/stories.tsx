import * as React from "react";
import { storiesOf } from "@storybook/react";
import { Expandable } from "./expandable";
import { Checkbox } from "./checkbox";
import { MuiCheckbox } from "./muiCheckBox";
import { useState } from "react";
import { ApiCheckbox } from "./ApiCheckbox";
import BloomButton from "./bloomButton";
import { showConfirmDialog, IConfirmDialogProps } from "./confirmDialog";
import ImportIcon from "./icons/ImportIcon";
import DeleteIcon from "@material-ui/icons/Delete";
import PlaybackOrderControls from "./playbackOrderControls";

storiesOf("Localizable Widgets", module)
    .add("Expandable", () => (
        <Expandable
            l10nKey="bogus"
            expandedHeight="30px"
            headingText="I am so advanced"
        >
            Look at this!
        </Expandable>
    ))
    .add("BloomButton", () => (
        <div>
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Look at this!
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="text"
            >
                Variant = text
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Variant = outlined
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<DeleteIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Material icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Custom icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                size="small"
                variant="outlined"
            >
                Small
            </BloomButton>
        </div>
    ));

storiesOf("Localizable Widgets/Checkbox", module)
    .add("off", () => <Checkbox l10nKey="bogus">Click me</Checkbox>)
    .add("on", () => (
        <Checkbox checked={true} l10nKey="bogus">
            Click me
        </Checkbox>
    ))
    .add("indeterminate", () => (
        <Checkbox tristate={true} l10nKey="bogus">
            Click me
        </Checkbox>
    ));
// see https://github.com/storybooks/storybook/issues/5721

storiesOf("Localizable Widgets/MuiCheckbox", module)
    .add("off", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(false);
            return (
                <MuiCheckbox
                    label=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("on", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(true);
            return (
                <MuiCheckbox
                    label=" Click me"
                    checked={checked}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    )
    .add("indeterminate", () =>
        React.createElement(() => {
            const [checked, setChecked] = useState<boolean | undefined>(
                undefined
            );
            return (
                <MuiCheckbox
                    label=" Click me"
                    checked={checked}
                    tristate={true}
                    onCheckChanged={newState => setChecked(newState)}
                    l10nKey="bogus"
                />
            );
        })
    );
storiesOf("Localizable Widgets/ApiCheckbox", module).add("ApiCheckbox", () =>
    React.createElement(() => (
        <ApiCheckbox
            english="Motion Book"
            l10nKey="PublishTab.Android.MotionBookMode"
            apiEndpoint="publish/android/motionBookMode"
        />
    ))
);

const confirmDialogProps: IConfirmDialogProps = {
    title: "Title",
    titleL10nKey: "",
    message: "Message",
    messageL10nKey: "",
    confirmButtonLabel: "OK",
    confirmButtonLabelL10nKey: "",
    onDialogClose: dialogResult => {
        alert(dialogResult);
    }
};
storiesOf("Misc", module).add("ConfirmDialog", () =>
    React.createElement(() => (
        <div>
            <div id="modal-container" />
            <BloomButton
                onClick={() =>
                    showConfirmDialog(
                        confirmDialogProps,
                        document.getElementById("modal-container")
                    )
                }
                enabled={true}
                hasText={true}
                l10nKey={"dummyKey"}
            >
                Open Confirm Dialog
            </BloomButton>
        </div>
    ))
);

const divStyles: React.CSSProperties = {
    width: "150px",
    height: "80px",
    border: "1px solid red",
    display: "flex",
    justifyContent: "center"
};

const bumpUp = (whichPositionToBump: number): void => {
    console.log(
        `Bump up myOrderNum from ${whichPositionToBump} to ${++whichPositionToBump}`
    );
};

const bumpDown = (whichPositionToBump: number): void => {
    console.log(
        `Bump down myOrderNum from ${whichPositionToBump} to ${--whichPositionToBump}`
    );
};

storiesOf("PlaybackOrderControls", module).add("PlaybackOrder buttons", () =>
    React.createElement(() => (
        <>
            <div style={divStyles}>
                <PlaybackOrderControls
                    sizeOfList={3}
                    myOrderNum={2}
                    bumpUp={bumpUp}
                    bumpDown={bumpDown}
                />
            </div>
            <div style={divStyles}>
                <PlaybackOrderControls
                    sizeOfList={3}
                    myOrderNum={1}
                    bumpUp={bumpUp}
                    bumpDown={bumpDown}
                />
            </div>
            <div style={divStyles}>
                <PlaybackOrderControls
                    sizeOfList={3}
                    myOrderNum={3}
                    bumpUp={bumpUp}
                    bumpDown={bumpDown}
                />
            </div>
        </>
    ))
);
