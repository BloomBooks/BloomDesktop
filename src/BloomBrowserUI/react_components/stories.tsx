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
import CustomColorPicker from "./customColorPicker";
import { ISwatchDefn, getBackgroundFromSwatch } from "./colorSwatch";
import {
    showColorPickerDialog,
    IColorPickerDialogProps
} from "./colorPickerDialog";
import SmallNumberPicker from "./smallNumberPicker";

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

// Try to simulate the environment of the page preview
const containerDivStyles: React.CSSProperties = {
    width: "500px",
    height: "100%",
    border: "1px solid green",
    flexDirection: "column",
    display: "flex",
    alignItems: "center"
};

const moveToBottomStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    justifyContent: "flex-end",
    flex: 1,
    border: "1px solid red",
    width: 200
};

const previewControlsStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "row",
    paddingBottom: "20px"
};

const pickerStyles: React.CSSProperties = {
    marginTop: "-10px",
    marginLeft: "-15px",
    position: "absolute"
};

storiesOf("Misc", module)
    .add("ConfirmDialog", () =>
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
    )
    .add("Small Number Picker", () =>
        React.createElement(() => {
            const numberOfPagesTooltip = "Number of pages to add";
            const onHandleChange = (newNumber: number) => {
                console.log("We handled change!");
                console.log(`  result was ${newNumber}`);
            };
            const min = 1;
            const max = 15;

            return (
                <div style={containerDivStyles}>
                    <div style={moveToBottomStyles}>
                        <div style={previewControlsStyles}>
                            <BloomButton
                                l10nKey="dummyKey"
                                hasText={true}
                                enabled={false}
                                onClick={() => {
                                    console.log("Does nothing");
                                }}
                            >
                                My Button
                            </BloomButton>
                            <div style={pickerStyles}>
                                <SmallNumberPicker
                                    minLimit={min}
                                    maxLimit={max}
                                    handleChange={onHandleChange}
                                    tooltip={numberOfPagesTooltip}
                                />
                            </div>
                        </div>
                    </div>
                </div>
            );
        })
    );

const playbackControlsDivStyles: React.CSSProperties = {
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
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={2}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={1}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={3}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
        </>
    ))
);

const mainBlockStyles: React.CSSProperties = {
    width: 300,
    height: 300,
    display: "flex",
    flexDirection: "column",
    justifyContent: "center"
};

const backDivStyles: React.CSSProperties = {
    position: "relative",
    flex: 3,
    display: "flex",
    background: "lightgreen"
};

const chooserStyles: React.CSSProperties = {
    position: "absolute",
    top: 80,
    left: 400,
    width: 250,
    height: 310,
    border: "2px solid green",
    display: "flex",
    justifyContent: "center",
    alignContent: "center"
};

const initialOverDivStyles: React.CSSProperties = {
    position: "absolute",
    top: 10,
    left: 15,
    width: 120,
    height: 70,
    border: "1px solid red",
    zIndex: 2,
    background: "#fff"
};

const defaultSwatches: ISwatchDefn[] = [
    { name: "white", colors: ["#ffffff"] },
    { name: "grey", colors: ["#777777"] },
    { name: "black", colors: ["#000000"] },
    { name: "whiteToCalico", colors: ["white", "#DFB28B"] },
    { name: "60%Portafino", colors: ["#7b8eb8"], opacity: 0.6 }
];

storiesOf("Custom Color Chooser", module)
    .add("Background/text color", () =>
        React.createElement(() => {
            const [chooserShowing, setChooserShowing] = useState(false);
            const [backgroundChooser, setBackgroundChooser] = useState(true); // false is text chooser
            const [overDivStyles, setOverDivStyles] = useState(
                initialOverDivStyles
            );
            const [
                chooserCurrentBackgroundColor,
                setChooserCurrentBackgroundColor
            ] = useState(defaultSwatches[0]);
            const [
                chooserCurrentTextColor,
                setChooserCurrentTextColor
            ] = useState(defaultSwatches[2]);
            const handleColorChange = (
                color: ISwatchDefn,
                colorIsBackground: boolean
            ) => {
                if (colorIsBackground) {
                    // set background color
                    setOverDivStyles({
                        ...overDivStyles,
                        background: getBackgroundFromSwatch(color)
                    });
                    setChooserCurrentBackgroundColor(color);
                } else {
                    const textColor = color.colors[0]; // don't need gradients or opacity for text color
                    // set text color
                    setOverDivStyles({
                        ...overDivStyles,
                        color: textColor
                    });
                    setChooserCurrentTextColor(color);
                }
            };

            return (
                <div style={mainBlockStyles}>
                    <div id="background-image" style={backDivStyles}>
                        I am a background "image" with lots of text so we can
                        test transparency.
                    </div>
                    <div id="set-my-background" style={overDivStyles}>
                        Set my text and background colors with the buttons
                    </div>
                    <div
                        style={{
                            flexDirection: "row",
                            display: "inline-flex",
                            justifyContent: "space-around"
                        }}
                    >
                        <BloomButton
                            onClick={() => {
                                setBackgroundChooser(true);
                                setChooserShowing(!chooserShowing);
                            }}
                            enabled={true}
                            hasText={true}
                            l10nKey={"dummyKey"}
                        >
                            Background
                        </BloomButton>
                        <BloomButton
                            onClick={() => {
                                setBackgroundChooser(false);
                                setChooserShowing(!chooserShowing);
                            }}
                            enabled={true}
                            hasText={true}
                            l10nKey={"dummyKey"}
                        >
                            Text
                        </BloomButton>
                    </div>
                    {chooserShowing && (
                        <div style={chooserStyles}>
                            <CustomColorPicker
                                onChange={color =>
                                    handleColorChange(color, backgroundChooser)
                                }
                                currentColor={
                                    backgroundChooser
                                        ? chooserCurrentBackgroundColor
                                        : chooserCurrentTextColor
                                }
                                swatchColors={defaultSwatches}
                                noAlphaSlider={!backgroundChooser}
                                noGradientSwatches={!backgroundChooser}
                            />
                        </div>
                    )}
                </div>
            );
        })
    )
    .add("Color Picker Dialog", () =>
        React.createElement(() => {
            const [overDivStyles, setOverDivStyles] = useState(
                initialOverDivStyles
            );
            const [
                chooserCurrentBackgroundColor,
                setChooserCurrentBackgroundColor
            ] = useState(defaultSwatches[0]);
            const handleColorChange = (color: ISwatchDefn) => {
                console.log("Color change:");
                console.log(
                    `  ${color.name}: ${color.colors[0]}, ${color.colors[1]}, ${color.opacity}`
                );
                // set background color
                setOverDivStyles({
                    ...overDivStyles,
                    background: getBackgroundFromSwatch(color)
                });
                setChooserCurrentBackgroundColor(color);
            };

            const colorPickerDialogProps: IColorPickerDialogProps = {
                localizedTitle: "Custom Color Picker",
                initialColor: chooserCurrentBackgroundColor,
                defaultSwatchColors: defaultSwatches,
                onChange: color => handleColorChange(color)
            };

            return (
                <div style={mainBlockStyles}>
                    <div id="background-image" style={backDivStyles}>
                        I am a background "image" with lots of text so we can
                        test transparency.
                    </div>
                    <div id="set-my-background" style={overDivStyles}>
                        Set my text and background colors with the button
                    </div>
                    <div id="modal-container" />
                    <BloomButton
                        onClick={() =>
                            showColorPickerDialog(
                                colorPickerDialogProps,
                                document.getElementById("modal-container")
                            )
                        }
                        enabled={true}
                        hasText={true}
                        l10nKey={"dummyKey"}
                    >
                        Open Color Picker Dialog
                    </BloomButton>
                </div>
            );
        })
    );
