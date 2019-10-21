import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Callout.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";
import { BubbleSpec } from "comical-js//bubbleSpec";
import { Link } from "../../../react_components/link";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@material-ui/core/FormControl";
import Select from "@material-ui/core/Select";
import { MenuItem } from "@material-ui/core";
import { values } from "mobx";
import { Div, Span } from "../../../react_components/l10nComponents";
import { createStyles, makeStyles, Theme } from "@material-ui/core/styles"; // TODO: Am I really needed?
import InputLabel from "@material-ui/core/InputLabel";
import * as toastr from "toastr";

const CalloutToolControls: React.FunctionComponent = () => {
    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [textColor, setTextColor] = useState("black");
    const [backgroundColor, setBackgroundColor] = useState("white");
    const [outlineColor, setOutlineColor] = useState("black");
    const [bubbleActive, setBubbleActive] = useState(false);

    // if bubbleActive is true, corresponds to the active bubble. Otherwise, corresponds to the most recently active bubble.
    const [currentBubbleSpec, setCurrentBubbleSpec] = useState(undefined as (
        | BubbleSpec
        | undefined));

    // Callback to initialize bubbleEditing and get the initial bubbleSpec
    const bubbleSpecInitialization = () => {
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            console.assert(
                "ERROR: Bubble manager is not initialized yet. Please investigate!"
            );
            return;
        }

        bubbleManager.turnOnBubbleEditing();

        const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();

        // The callback function is (currently) called when switching between bubbles, but is not called if the tail spec changes,
        // or for style and similar changes to the bubble that are initiated by React.
        bubbleManager.requestBubbleChangeNotification(
            (bubble: BubbleSpec | undefined) => {
                setCurrentBubbleSpec(bubble);
            }
        );

        setCurrentBubbleSpec(bubbleSpec);
    };

    // Enhance: if we don't want to have a static, or don't want
    // this function to know about CalloutTool, we could just pass
    // a setter for this as a property.
    CalloutTool.theOneCalloutTool!.callOnNewPageReady = () => {
        bubbleSpecInitialization();
    };
    useEffect(() => {
        if (currentBubbleSpec) {
            setStyle(currentBubbleSpec.style);
            setBubbleActive(true);
        } else {
            setBubbleActive(false);
        }
    }, [currentBubbleSpec]);

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

        // TODO: May need to massage the values before passing them to Comical
        // Update the Comical canvas on the page frame
        CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
            outerBorderColor: newValue
        });
    };

    const handleChildBubbleLinkClick = event => {
        const bubbleManager = CalloutTool.bubbleManager();

        const parentElement = bubbleManager.getActiveElement();

        if (!parentElement) {
            // No parent to attach to
            toastr.info("No element is currently active.");
            return;
        }

        // Enhance: Is there a cleaner way to keep activeBubbleSpec up to date? Comical would need to call the notifier a lot more often like when the tail moves.

        // Retrieve the latest bubbleSpec
        const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();
        const [offsetX, offsetY] = CalloutTool.GetChildPositionFromParentBubble(
            parentElement,
            bubbleSpec
        );
        bubbleManager.addChildTOPBoxAndReloadPage(
            parentElement,
            offsetX,
            offsetY
        );
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
                                    Caption
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
                    <Link
                        l10nKey="EditTab.Toolbox.CalloutTool.Options.AddChildBubble"
                        onClick={event => {
                            handleChildBubbleLinkClick(event);
                        }}
                    >
                        Add Child Bubble
                    </Link>
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
    public static theOneCalloutTool: CalloutTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        CalloutTool.theOneCalloutTool = this;
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CalloutBody");

        ReactDOM.render(<CalloutToolControls />, root);
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
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(() => this.newPageReady(), 100);
            return;
        }

        if (this.callOnNewPageReady) {
            this.callOnNewPageReady();
        } else {
            console.assert(
                false,
                "CallOnNewPageReady is always expected to be defined but it is not."
            );
        }
    }

    public detachFromPage() {
        const bubbleManager = CalloutTool.bubbleManager();
        if (bubbleManager) {
            bubbleManager.turnOffBubbleEditing();
            bubbleManager.detachBubbleChangeNotification();
        }
    }

    public static bubbleManager(): TextOverPictureManager {
        return getPageFrameExports().getTheOneBubbleManager();
    }

    // Returns a 2-tuple containing the desired x and y offsets of the child bubble from the parent bubble
    //   (i.e., offsetX = child.left - parent.left)
    public static GetChildPositionFromParentBubble(
        parentElement: HTMLElement,
        parentBubbleSpec: BubbleSpec | undefined
    ): number[] {
        let offsetX = parentElement.clientWidth;
        let offsetY = parentElement.clientHeight;

        if (
            parentBubbleSpec &&
            parentBubbleSpec.tails &&
            parentBubbleSpec.tails.length > 0
        ) {
            const tail = parentBubbleSpec.tails[0];

            const bubbleCenterX =
                parentElement.offsetLeft + parentElement.clientWidth / 2.0;
            const bubbleCenterY =
                parentElement.offsetTop + parentElement.clientHeight / 2.0;

            const deltaX = tail.tipX - bubbleCenterX;
            const deltaY = tail.tipY - bubbleCenterY;

            // Place the new child in the opposite quandrant of the tail
            if (deltaX > 0) {
                // ENHANCE: SHould be the child's width
                offsetX = -parentElement.clientWidth;
            } else {
                offsetX = parentElement.clientWidth;
            }

            if (deltaY > 0) {
                // ENHANCE: SHould be the child's height
                offsetY = -parentElement.clientHeight;
            } else {
                offsetY = parentElement.clientHeight;
            }
        }

        return [offsetX, offsetY];
    }
}
