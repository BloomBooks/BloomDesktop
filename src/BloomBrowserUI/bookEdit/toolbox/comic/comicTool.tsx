import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./comic.less";
import {
    getPageFrameExports,
    getEditViewFrameExports
} from "../../js/bloomFrames";
import { BubbleManager } from "../../js/bubbleManager";
import { BubbleSpec, TailSpec } from "comicaljs";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@material-ui/core/FormControl";
import Select from "@material-ui/core/Select";
import { MenuItem, Button } from "@material-ui/core";
import { useL10n } from "../../../react_components/l10nHooks";
import { Div, Span } from "../../../react_components/l10nComponents";
import InputLabel from "@material-ui/core/InputLabel";
import * as toastr from "toastr";
import { default as TrashIcon } from "@material-ui/icons/Delete";
import { BloomApi } from "../../../utils/bloomApi";
import { isLinux } from "../../../utils/isLinux";
import { MuiCheckbox } from "../../../react_components/muiCheckBox";
import { ColorBar } from "./colorBar";
import { ISwatchDefn } from "../../../react_components/colorSwatch";
import {
    specialColors,
    defaultBackgroundColors,
    defaultTextColors,
    getSwatchFromHex,
    isSpecialColorName,
    getSpecialColorName
} from "./comicToolColorHelper";
import { IColorPickerDialogProps } from "../../../react_components/colorPickerDialog";

const ComicToolControls: React.FunctionComponent = () => {
    const l10nPrefix = "ColorPicker.";

    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [outlineColor, setOutlineColor] = useState<string | undefined>(
        undefined
    );
    const [bubbleActive, setBubbleActive] = useState(false);
    const [showTailChecked, setShowTailChecked] = useState(false);

    const [isXmatter, setIsXmatter] = useState(true);

    // Setup for color picker, in case we need it.
    const textColorTitle = useL10n(
        "Text Color",
        "EditTab.Toolbox.ComicTool.Options.TextColor"
    );
    const backgroundColorTitle = useL10n(
        "Background Color",
        "EditTab.Toolbox.ComicTool.Options.BackgroundColor"
    );

    // Text color swatch
    // defaults to "black" text color
    const [textColorSwatch, setTextColorSwatch] = useState(
        defaultTextColors[0]
    );

    // Background color swatch
    // defaults to "white" background color
    const [backgroundColorSwatch, setBackgroundColorSwatch] = useState(
        defaultBackgroundColors[1]
    );

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

    // Reset UI when current bubble spec changes (e.g. user clicked on a bubble).
    useEffect(() => {
        if (currentBubbleSpec) {
            setStyle(currentBubbleSpec.style);
            setShowTailChecked(
                currentBubbleSpec.tails && currentBubbleSpec.tails.length > 0
            );
            setOutlineColor(currentBubbleSpec.outerBorderColor);
            setBubbleActive(true);
            // N.B. Don't forget to add spec opacity to call to 'getSwatchFromHex' (and maybe rename the method).
            const backColor = getBackgroundColorValue(currentBubbleSpec);
            if (!isSpecialColorName(backColor)) {
                const newSwatch = getSwatchFromHex(backColor);
                // Enhance: When the bubble spec has opacity, update it here
                // newSwatch.opacity = (currentBubbleSpec.opacity ? currentBubbleSpec.opacity : 1);
                setBackgroundColorSwatch(newSwatch);
            } else {
                // A "special" color gradient, get our swatch from the definitions.
                // It "has" to be there, because we just checked to see if the name was a special color!
                const newSwatch = specialColors.find(
                    color => color.name === backColor
                ) as ISwatchDefn;
                setBackgroundColorSwatch(newSwatch);
            }

            // Get the current bubble's textColor and set it
            const bubbleTextColor = ComicTool.bubbleManager().getTextColor();
            const newSwatch = getSwatchFromHex(bubbleTextColor);
            setTextColorSwatch(newSwatch);
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
        const newSpec = ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            style: newStyle
        });
        setCurrentBubbleSpec(newSpec); // we do this because the new style's spec may affect Show Tail too
    };

    // Callback for show tail checkbox changed
    // Presently, only disabled if style is "none".
    const handleShowTailChanged = (value: boolean) => {
        setShowTailChecked(value);

        // Update the Comical canvas on the page frame
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            tails: value
                ? [ComicTool.bubbleManager().getDefaultTailSpec() as TailSpec]
                : []
        });
    };

    const getBackgroundColorValue = (spec: BubbleSpec) => {
        if (!spec.backgroundColors || spec.backgroundColors.length === 0) {
            return "white";
        }
        if (spec.backgroundColors.length === 1) {
            return spec.backgroundColors[0];
        }
        const specialName = getSpecialColorName(spec.backgroundColors);
        return specialName ? specialName : "white"; // maybe from a later version of Bloom? All we can do.
    };

    // We come into this from chooser change
    const updateTextColor = (newColorSwatch: ISwatchDefn) => {
        const color = newColorSwatch.colors[0]; // text color is always monochrome
        // Update the toolbox controls
        setTextColorSwatch(newColorSwatch);

        ComicTool.bubbleManager().setTextColor(color);
    };

    // We come into this from chooser change
    const updateBackgroundColor = (newColorSwatch: ISwatchDefn) => {
        // Update the toolbox controls
        setBackgroundColorSwatch(newColorSwatch);

        // Update the Comical canvas on the page frame
        const backgroundColors = newColorSwatch.colors;
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            backgroundColors: backgroundColors
            // opacity: newColorSwatch.opacity?!
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

    const ondragstart = (ev: React.DragEvent<HTMLElement>, style: string) => {
        // Here "bloomBubble" is a unique, private data type recognised
        // by ondragover and ondragdrop methods that BubbleManager
        // attaches to bloom image containers. It doesn't make sense to
        // drag these objects anywhere else, so they don't need any of
        // the common data types.
        ev.dataTransfer.setData("bloomBubble", style);
    };

    const ondragend = (ev: React.DragEvent<HTMLElement>, style: string) => {
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

    const styleSupportsShowTail = (style: string) => {
        switch (style) {
            case "none":
            case "":
                return false;
            default:
                return true;
        }
    };

    const launchTextColorChooser = () => {
        const colorPickerDialogProps: IColorPickerDialogProps = {
            noAlphaSlider: true,
            noGradientSwatches: true,
            localizedTitle: textColorTitle,
            initialColor: textColorSwatch,
            defaultSwatchColors: defaultTextColors,
            onChange: color => updateTextColor(color)
        };
        getEditViewFrameExports().showColorPickerDialog(colorPickerDialogProps);
    };

    // We have temporarily disabled the alpha slider for the background color chooser.
    // Unfortunately, with an alpha slider, the hex input will automatically switch to rgb
    // the moment the user sets alpha to anything but max opacity.
    const launchBackgroundColorChooser = () => {
        const colorPickerDialogProps: IColorPickerDialogProps = {
            noAlphaSlider: true, // Temporary, when BL-8537 is in testing, remove this line.
            localizedTitle: backgroundColorTitle,
            initialColor: backgroundColorSwatch,
            defaultSwatchColors: defaultBackgroundColors,
            onChange: color => updateBackgroundColor(color)
        };
        getEditViewFrameExports().showColorPickerDialog(colorPickerDialogProps);
    };

    const needToCalculateTransparency = (): boolean => {
        const opacityDecimal = backgroundColorSwatch.opacity;
        return !!opacityDecimal && opacityDecimal < 1.0;
    };

    const percentTransparentFromOpacity = (): string => {
        if (!needToCalculateTransparency()) return "0"; // We shouldn't call this under these circumstances.
        return (100 - (backgroundColorSwatch.opacity as number) * 100).toFixed(
            0
        );
    };

    // We need to calculate this, even though we may not need to display it to keep from violating React's
    // rule about not changing the number of hooks rendered.
    const percentTransparencyString = (): string | undefined => {
        const percent = percentTransparentFromOpacity();
        const transparencyString = useL10n(
            "Percent Transparent",
            l10nPrefix + "PercentTransparent",
            "",
            percent
        );
        return percent === "0" ? undefined : transparencyString;
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
                        <div className="showTailCheckbox">
                            <MuiCheckbox
                                label="Show Tail"
                                l10nKey="EditTab.Toolbox.ComicTool.Options.Style.ShowTail"
                                disabled={!styleSupportsShowTail(style)}
                                checked={showTailChecked}
                                onCheckChanged={v => {
                                    handleShowTailChanged(v as boolean);
                                }}
                            />
                        </div>
                    </FormControl>
                    <FormControl>
                        <InputLabel htmlFor="text-color-bar" shrink={true}>
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.TextColor">
                                Text Color
                            </Span>
                        </InputLabel>
                        <ColorBar
                            id="text-color-bar"
                            onClick={launchTextColorChooser}
                            name={textColorSwatch.name}
                            colors={textColorSwatch.colors}
                        />
                    </FormControl>
                    <FormControl>
                        <InputLabel
                            shrink={true}
                            htmlFor="background-color-bar"
                        >
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor">
                                Background Color
                            </Span>
                        </InputLabel>
                        <ColorBar
                            id="background-color-bar"
                            onClick={launchBackgroundColorChooser}
                            name={backgroundColorSwatch.name}
                            text={percentTransparencyString()}
                            colors={backgroundColorSwatch.colors}
                        />
                    </FormControl>
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
