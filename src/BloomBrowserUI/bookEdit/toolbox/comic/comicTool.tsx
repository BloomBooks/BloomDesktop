import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./comic.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { BubbleManager } from "../../js/bubbleManager";
import { BubbleSpec } from "comicaljs";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@material-ui/core/FormControl";
import Select from "@material-ui/core/Select";
import { MenuItem, Button } from "@material-ui/core";
import { Div, Span } from "../../../react_components/l10nComponents";
import InputLabel from "@material-ui/core/InputLabel";
import * as toastr from "toastr";
import { default as TrashIcon } from "@material-ui/icons/Delete";
import { BloomApi } from "../../../utils/bloomApi";
import { isLinux } from "../../../utils/isLinux";

const ComicToolControls: React.FunctionComponent = () => {
    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [textColor, setTextColor] = useState("black");
    const [backgroundColor, setBackgroundColor] = useState("white");
    const [outlineColor, setOutlineColor] = useState<string | undefined>(
        undefined
    );
    const [bubbleActive, setBubbleActive] = useState(false);

    const [isXmatter, setIsXmatter] = useState(true);

    // if bubbleActive is true, corresponds to the active bubble. Otherwise, corresponds to the most recently active bubble.
    const [currentBubbleSpec, setCurrentBubbleSpec] = useState(undefined as (
        | BubbleSpec
        | undefined));

    // Callback to initialize bubbleEditing and get the initial bubbleSpec
    const bubbleSpecInitialization = () => {
        const bubbleManager = ComicTool.bubbleManager();
        if (!bubbleManager) {
            console.assert(
                false,
                "ERROR: Bubble manager is not initialized yet. Please investigate!"
            );
            return;
        }

        bubbleManager.turnOnBubbleEditing();
        bubbleManager.turnOnHidingImageButtons();

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
    // this function to know about ComicTool, we could just pass
    // a setter for this as a property.
    ComicTool.theOneComicTool!.callOnNewPageReady = () => {
        bubbleSpecInitialization();
        setIsXmatter(ToolboxToolReactAdaptor.isXmatter());
    };
    useEffect(() => {
        if (currentBubbleSpec) {
            setStyle(currentBubbleSpec.style);
            setOutlineColor(currentBubbleSpec.outerBorderColor);
            setBubbleActive(true);
            setBackgroundColor(getBackgroundColorValue(currentBubbleSpec));
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
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
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
        // ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
        //     textColor: newTextColor
        // });
    };

    const specialColors = [
        // #DFB28B is the color Comical has been using as the default for captions.
        // It's fairly close to the "Calico" color defined at https://www.htmlcsscolor.com/hex/D5B185 (#D5B185)
        // so I decided it was the best choice for keeping that option.
        { name: "whiteToCalico", colors: ["white", "#DFB28B"] },
        // https://www.htmlcsscolor.com/hex/ACCCDD
        { name: "whiteToFrenchPass", colors: ["white", "#ACCCDD"] },
        // https://encycolorpedia.com/7b8eb8
        { name: "whiteToPortafino", colors: ["white", "#7b8eb8"] }
    ];

    const getBackgroundColorValue = (spec: BubbleSpec) => {
        if (!spec.backgroundColors || spec.backgroundColors.length == 0) {
            return "white";
        }
        if (spec.backgroundColors.length == 1) {
            return spec.backgroundColors[0];
        }
        // love to use forEach, but we want to return from this function if we match.
        for (let i = 0; i < specialColors.length; i++) {
            const combo = specialColors[i];
            // For the special colors we currently have, checking the second item is enough.
            if (combo.colors[1] === spec.backgroundColors![1]) {
                return combo.name;
            }
        }
        // maybe from a later version of Bloom? All we can do.
        return "white";
    };

    // Callback when background color of the bubble is changed
    const handleBackgroundColorChanged = event => {
        const newValue = event.target.value;

        // Update the toolbox controls
        setBackgroundColor(newValue);

        // Update the Comical canvas on the page frame
        let backgroundColors = [newValue];
        // love to use forEach, but we want to return from this function if we match.
        for (let i = 0; i < specialColors.length; i++) {
            const combo = specialColors[i];
            if (combo.name === newValue) {
                backgroundColors = combo.colors;
                break;
            }
        }
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            backgroundColors: backgroundColors
        });
    };

    // Callback when outline color of the bubble is changed
    const handleOutlineColorChanged = event => {
        let newValue = event.target.value;

        if (newValue === "none") {
            newValue = undefined;
        }

        // Update the toolbox controls
        setOutlineColor(newValue);

        // TODO: May need to massage the values before passing them to Comical
        // Update the Comical canvas on the page frame
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            outerBorderColor: newValue
        });
    };

    const handleChildBubbleLinkClick = event => {
        const bubbleManager = ComicTool.bubbleManager();

        const parentElement = bubbleManager.getActiveElement();

        if (!parentElement) {
            // No parent to attach to
            toastr.info("No element is currently active.");
            return;
        }

        // Enhance: Is there a cleaner way to keep activeBubbleSpec up to date? Comical would need to call the notifier a lot more often like when the tail moves.

        // Retrieve the latest bubbleSpec
        const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();
        const [offsetX, offsetY] = ComicTool.GetChildPositionFromParentBubble(
            parentElement,
            bubbleSpec
        );
        bubbleManager.addChildTOPBoxAndReloadPage(
            parentElement,
            offsetX,
            offsetY
        );
    };

    const ondragstart = (ev, style) => {
        // Here "bloomBubble" is a unique, private data type recognised
        // by ondragover and ondragdrop methods that BubbleManager
        // attaches to bloom image containers. It doesn't make sense to
        // drag these objects anywhere else, so they don't need any of
        // the common data types.
        ev.dataTransfer.setData("bloomBubble", style);
    };

    const ondragend = (ev, style) => {
        // The Linux/Mono/Geckofx environment does not produce the dragenter, dragover,
        // and drop events for the targeted element.  It does produce the dragend event
        // for the source element with screen coordinates of where the mouse was released.
        // This can be used to simulate the drop event with coordinate transformation.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-7958.
        if (
            isLinux() &&
            ComicTool.bubbleManager().addFloatingTOPBoxWithScreenCoords(
                ev.screenX,
                ev.screenY,
                style
            )
        ) {
            BloomApi.postThatMightNavigate(
                "common/saveChangesAndRethinkPageEvent"
            );
        }
    };

    const deleteBubble = () => {
        const bubbleManager = ComicTool.bubbleManager();
        const active = bubbleManager.getActiveElement();
        if (active) {
            bubbleManager.deleteTOPBox(active);
        }
    };

    return (
        <div id="comicToolControls">
            <div
                id={"comicToolControlShapeChooserRegion"}
                className={!isXmatter ? "" : "disabled"}
            >
                <Div
                    l10nKey="EditTab.Toolbox.ComicTool.DragInstructions"
                    className="comicToolControlDragInstructions"
                >
                    Drag to add to an image
                </Div>
                <div className={"shapeChooserRow"} id={"shapeChooserRow1"}>
                    <img
                        id="shapeChooserSpeechBubble"
                        className="comicToolControlDraggableBubble"
                        src="comic-icon.svg"
                        draggable={true}
                        onDragStart={ev => ondragstart(ev, "speech")}
                        onDragEnd={ev => ondragend(ev, "speech")}
                    />
                    <Span
                        id="shapeChooserTextBlock"
                        l10nKey="EditTab.Toolbox.ComicTool.TextBlock"
                        className="comicToolControlDraggableBubble"
                        draggable={true}
                        onDragStart={ev => ondragstart(ev, "none")}
                        onDragEnd={ev => ondragend(ev, "none")}
                    >
                        Text Block
                    </Span>
                </div>
                <div className={"shapeChooserRow"} id={"shapeChooserRow2"}>
                    <Span
                        id="shapeChooserCaption"
                        l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Caption"
                        className="comicToolControlDraggableBubble"
                        draggable={true}
                        onDragStart={ev => ondragstart(ev, "caption")}
                        onDragEnd={ev => ondragend(ev, "caption")}
                    >
                        Caption
                    </Span>
                </div>
            </div>
            <div
                id={"comicToolControlOptionsRegion"}
                className={bubbleActive && !isXmatter ? "" : "disabled"}
            >
                <form autoComplete="off">
                    <FormControl>
                        <InputLabel htmlFor="bubble-style-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.Style">
                                Style
                            </Span>
                        </InputLabel>
                        <Select
                            value={style}
                            onChange={event => {
                                handleStyleChanged(event);
                            }}
                            className="bubbleOptionDropdown"
                            inputProps={{
                                name: "style",
                                id: "bubble-style-dropdown"
                            }}
                            MenuProps={{
                                className: "bubble-options-dropdown-menu"
                            }}
                        >
                            <MenuItem value="caption">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Caption">
                                    Caption
                                </Div>
                            </MenuItem>
                            <MenuItem value="pointedArcs">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Exclamation">
                                    Exclamation
                                </Div>
                            </MenuItem>
                            <MenuItem value="none">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.JustText">
                                    Just Text
                                </Div>
                            </MenuItem>
                            <MenuItem value="speech">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Speech">
                                    Speech
                                </Div>
                            </MenuItem>
                            <MenuItem value="ellipse">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Ellipse">
                                    Ellipse
                                </Div>
                            </MenuItem>
                            <MenuItem value="thought">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Thought">
                                    Thought
                                </Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    <br />
                    {/*
                    <FormControl>
                        <InputLabel htmlFor="bubble-textColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.TextColor">
                                Text Color
                            </Span>
                        </InputLabel>
                        <Select
                            value={textColor}
                            className="bubbleOptionDropdown"
                            inputProps={{
                                name: "textColor",
                                id: "bubble-textColor-dropdown"
                            }}
                            MenuProps={{
                                className: "bubble-options-dropdown-menu"
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
                        <br /> */}
                    <FormControl>
                        <InputLabel htmlFor="bubble-backgroundColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor">
                                Background Color
                            </Span>
                        </InputLabel>
                        <Select
                            value={backgroundColor}
                            className="bubbleOptionDropdown"
                            inputProps={{
                                name: "backgroundColor",
                                id: "bubble-backgroundColor-dropdown"
                            }}
                            MenuProps={{
                                className: "bubble-options-dropdown-menu"
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
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor.OldLace">
                                    Old Lace
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToCalico">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor.WhiteToCalico">
                                    White to Calico
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToFrenchPass">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor.WhiteToFrenchPass">
                                    White to French Pass
                                </Div>
                            </MenuItem>
                            <MenuItem value="whiteToPortafino">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor.WhiteToPortafino">
                                    White to Portafino
                                </Div>
                            </MenuItem>
                        </Select>
                    </FormControl>
                    <br />
                    <FormControl>
                        <InputLabel htmlFor="bubble-outlineColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor">
                                Outer Outline Color
                            </Span>
                        </InputLabel>
                        <Select
                            value={outlineColor ? outlineColor : "none"}
                            className="bubbleOptionDropdown"
                            inputProps={{
                                name: "outlineColor",
                                id: "bubble-outlineColor-dropdown"
                            }}
                            MenuProps={{
                                className: "bubble-options-dropdown-menu"
                            }}
                            onChange={event => {
                                handleOutlineColorChanged(event);
                            }}
                        >
                            <MenuItem value="none">
                                <Div l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor.None">
                                    None
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
                    <Button
                        onClick={event => handleChildBubbleLinkClick(event)}
                    >
                        <Div l10nKey="EditTab.Toolbox.ComicTool.Options.AddChildBubble">
                            Add Child Bubble
                        </Div>
                    </Button>
                    <TrashIcon
                        id="trashIcon"
                        color="primary"
                        onClick={() => deleteBubble()}
                    />
                </form>
            </div>
            <div id="comicToolControlFillerRegion" />
            <div id={"comicToolControlFooterRegion"}>
                <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Comic_Tool/Comic_Tool_overview.htm" />
            </div>
        </div>
    );
};
export default ComicToolControls;

export class ComicTool extends ToolboxToolReactAdaptor {
    public static theOneComicTool: ComicTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        ComicTool.theOneComicTool = this;
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ComicBody");

        ReactDOM.render(<ComicToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return "comic";
    }

    public isExperimental(): boolean {
        return false;
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
        const bubbleManager = ComicTool.bubbleManager();
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
        const bubbleManager = ComicTool.bubbleManager();
        if (bubbleManager) {
            // For now we are leaving bubble editing on, because even with the toolbox hidden,
            // the user might edit text, delete bubbles, move handles, etc.
            // We turn it off only when about to save the page.
            //bubbleManager.turnOffBubbleEditing();

            bubbleManager.turnOffHidingImageButtons();
            bubbleManager.detachBubbleChangeNotification();
        }
    }

    public static bubbleManager(): BubbleManager {
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
