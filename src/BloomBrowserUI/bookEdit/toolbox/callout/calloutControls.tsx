import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Callout.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";
import { BubbleSpec } from "comical-js//bubbleSpec";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@material-ui/core/FormControl";
import Select from "@material-ui/core/Select";
import { MenuItem } from "@material-ui/core";
import { values } from "mobx";
import { Div, Span } from "../../../react_components/l10nComponents";
import { createStyles, makeStyles, Theme } from "@material-ui/core/styles"; // TODO: Am I really needed?
import InputLabel from "@material-ui/core/InputLabel";
import FormHelperText from "@material-ui/core/FormHelperText";

interface ICalloutToolProps {
    style: string;
    textColor: string;
    backgroundColor: string;
    outlineColor: string;
    bubbleActive: boolean;
}

const CalloutToolControls: React.FunctionComponent<ICalloutToolProps> = (
    props: ICalloutToolProps
) => {
    // Declare all the hooks
    const [style, setStyle] = useState(props.style);
    const [textColor, setTextColor] = useState(props.textColor);
    const [backgroundColor, setBackgroundColor] = useState(
        props.backgroundColor
    );
    const [outlineColor, setOutlineColor] = useState(props.outlineColor);
    const [bubbleActive, setBubbleActive] = useState(props.bubbleActive);

    // Callback to initialize bubbleEditing and get the initial bubbleSpec
    const bubbleSpecInitialization = () => {
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            return;
        }

        bubbleManager.turnOnBubbleEditing();

        const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();

        bubbleManager.requestBubbleChangeNotification(
            (bubble: BubbleSpec | undefined) => {
                // TODO: Ugh, this is a circular dependency-like scenario :(
                //setActiveBubbleSpec(bubble);

                if (bubble) {
                    setStyle(bubble.style);
                    setBubbleActive(true);
                }
            }
        );
        return bubbleSpec;
    };

    const [activeBubbleSpec, setActiveBubbleSpec] = useState(
        () => {
            return bubbleSpecInitialization();
        } // The function will only be evaluated the first time.
    );
    useEffect(() => {
        if (activeBubbleSpec) {
            setStyle(activeBubbleSpec.style);
            setBubbleActive(true);
        }

        // Return value is the Cleanup function
        return () => {
            const bubbleManager = CalloutTool.bubbleManager();
            if (bubbleManager) {
                bubbleManager.turnOffBubbleEditing();
                bubbleManager.detachBubbleChangeNotification();
            }
        };
    }, [activeBubbleSpec]);

    // Callback for style changed
    const handleStyleChanged = event => {
        const newStyle = event.target.value;

        // Update the toolbox controls
        setStyle(newStyle);

        // Update the Comical canvas on the page frame
        CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
            style: newStyle
        });
    };

    // Callback for text changed
    const handleTextColorChanged = event => {
        const newTextColor = event.target.value;

        // Update the toolbox controls
        setTextColor(newTextColor);

        // TODO: IMPLEMENT ME in Comical
        // // Update the Comical canvas on the page frame
        // CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
        //     textColor: newTextColor
        // });
    };

    // Callback when background color of the callout is changed
    const handleBackgroundColorChanged = event => {
        const newValue = event.target.value;

        // Update the toolbox controls
        setBackgroundColor(newValue);

        // TODO: Handle the gradients
        // Update the Comical canvas on the page frame
        CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
            backgroundColors: [newValue]
        });
    };

    // Callback when outline color of the callout is changed
    const handleOutlineColorChanged = event => {
        const newValue = event.target.value;

        // Update the toolbox controls
        setOutlineColor(newValue);

        // TODO: Handle the gradients
        // Update the Comical canvas on the page frame
        CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
            outerBorderColor: newValue
        });
    };

    return (
        <div>
            <div id={"calloutControlShapeChooserRegion"}>
                Drag one of these on top of an image: TODO: Implement dragger
            </div>
            <div
                id={"calloutControlOptionsRegion"}
                className={bubbleActive ? "" : "disabled"}
            >
                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.ControlsAvailable">
                    The selected item has these controls:
                </Div>
                <br />
                <form autoComplete="off">
                    <FormControl>
                        <InputLabel htmlFor="callout-style-dropdown">
                            <Span l10nKey="EditTab.Toolbox.CalloutTool.Options.Style">
                                Style
                            </Span>
                        </InputLabel>
                        <Select
                            value={style}
                            onChange={event => {
                                handleStyleChanged(event);
                            }}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "style",
                                id: "callout-style-dropdown"
                            }}
                            MenuProps={{
                                className: "callout-options-dropdown-menu"
                            }}
                        >
                            <MenuItem value="caption">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style.Caption">
                                    Caption (TODO)
                                </Div>
                            </MenuItem>
                            <MenuItem value="shout">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style.Exclamation">
                                    Exclamation
                                </Div>
                            </MenuItem>
                            <MenuItem value="none">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style.JustText">
                                    Just Text
                                </Div>
                            </MenuItem>
                            <MenuItem value="speech">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style.Speech">
                                    Speech
                                </Div>
                            </MenuItem>
                            <MenuItem value="thought">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style.Thought">
                                    Thought
                                </Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    <br />
                    {/*
                    <FormControl>
                        <InputLabel htmlFor="callout-textColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.CalloutTool.Options.TextColor">
                                Text Color
                            </Span>
                        </InputLabel>
                        <Select
                            value={textColor}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "textColor",
                                id: "callout-textColor-dropdown"
                            }}
                            MenuProps={{
                                className: "callout-options-dropdown-menu"
                            }}
                            onChange={event => {
                                handleTextColorChanged(event);
                            }}
                        >
                            <MenuItem value="white">
                                <Div l10nKey="Common.Colors.White">White</Div>
                            </MenuItem>
                            <MenuItem value="black">
                                <Div l10nKey="Common.Colors.Black">Black</Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    <br />
                    <FormControl>
                        <InputLabel htmlFor="callout-backgroundColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.CalloutTool.Options.BackgroundColor">
                                Background Color
                            </Span>
                        </InputLabel>
                        <Select
                            value={backgroundColor}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "backgroundColor",
                                id: "callout-backgroundColor-dropdown"
                            }}
                            MenuProps={{
                                className: "callout-options-dropdown-menu"
                            }}
                            onChange={event => {
                                handleBackgroundColorChanged(event);
                            }}
                        >
                            <MenuItem value="white">
                                <Div l10nKey="Common.Colors.White">White</Div>
                            </MenuItem>
                            <MenuItem value="black">
                                <Div l10nKey="Common.Colors.Black">Black</Div>
                            </MenuItem>
                            <MenuItem value="oldLace">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.BackgroundColor.OldLace">
                                    Old Lace
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToCalico">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.BackgroundColor.WhiteToCalico">
                                    White to Calico
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToFrenchPass">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.BackgroundColor.WhiteToFrenchPass">
                                    White to French Pass
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToPortafino">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.BackgroundColor.WhiteToPortafino">
                                    White to Portafino
                                </Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    <br />
                    <FormControl>
                        <InputLabel htmlFor="callout-outlineColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.CalloutTool.Options.OuterOutlineColor">
                                Outer Outline Color (Untranslated)
                            </Span>
                        </InputLabel>
                        <Select
                            value={outlineColor}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "outlineColor",
                                id: "callout-outlineColor-dropdown"
                            }}
                            MenuProps={{
                                className: "callout-options-dropdown-menu"
                            }}
                            onChange={event => {
                                handleOutlineColorChanged(event);
                            }}
                        >
                            <MenuItem value="none">
                                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.OuterOutlineColor.None">
                                    None (Untranslated)
                                </Div>
                            </MenuItem>
                            <MenuItem value="yellow">
                                <Div l10nKey="Common.Colors.Yellow">Yellow</Div>
                            </MenuItem>
                            <MenuItem value="crimson">
                                <Div l10nKey="Common.Colors.Crimson">
                                    Crimson
                                </Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    */}
                </form>
            </div>
            <div id={"calloutControlFooterRegion"}>
                {/* TODO: Update the help link */}
                <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Impairment_Visualizer/Impairment_Visualizer_overview.htm" />
            </div>
        </div>
    );
};
export default CalloutToolControls;

export class CalloutTool extends ToolboxToolReactAdaptor {
    //private reactControls: CalloutToolControls;
    private componentProps: ICalloutToolProps;

    public constructor() {
        super();

        // Just a default value
        this.componentProps = {
            style: "none",
            textColor: "black",
            backgroundColor: "white",
            outlineColor: "none",
            bubbleActive: false
        };
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CalloutBody");

        //ReactDOM.render(<CalloutToolControls {...this.componentProps} />, root);
        ReactDOM.render(
            <CalloutToolControls
                style={this.componentProps.style}
                textColor={this.componentProps.textColor}
                backgroundColor={this.componentProps.backgroundColor}
                outlineColor={this.componentProps.outlineColor}
                bubbleActive={this.componentProps.bubbleActive}
            />,
            root
        );
        return root as HTMLDivElement;
    }

    public id(): string {
        return "callout";
    }

    public isExperimental(): boolean {
        return true;
    }

    public toolRequiresEnterprise(): boolean {
        return false; // review
    }

    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    public newPageReady() {
        console.log("New page ready");
        CalloutTool.newPageReadyStaticHelper();

        // Or should it attempt to force re-render?
        // What if the bubbleSpec changes?

        // const bubbleManager = CalloutTool.bubbleManager();
        // if (!bubbleManager) {
        //     // probably the toolbox just finished loading before the page.
        //     // No clean way to fix this
        //     window.setTimeout(() => this.newPageReady(), 100);
        //     return;
        // }
        // bubbleManager.turnOnBubbleEditing();
        // const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();
        // if (bubbleSpec) {
        //     this.componentProps.bubbleActive = true;
        //     this.componentProps.style = bubbleSpec.style;
        // }
        //     <CalloutToolControls {...props} />;
        // }
        // if (this.reactControls) {
        //     this.reactControls.updateBubbleState(
        //         bubbleManager.getSelectedItemBubbleSpec()
        //     );
        // }
        // bubbleManager.requestBubbleChangeNotification(bubble => {
        //     if (this.reactControls) {
        //         this.reactControls.updateBubbleState(bubble);
        //     }
        // });
    }

    public static newPageReadyStaticHelper() {
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(
                () => CalloutTool.newPageReadyStaticHelper(),
                100
            );
            return;
        }
        bubbleManager.turnOnBubbleEditing();
    }

    public detachFromPage() {
        CalloutTool.bubbleManager().turnOffBubbleEditing();
    }

    public static bubbleManager(): TextOverPictureManager {
        return getPageFrameExports().getTheOneBubbleManager();
    }
}
