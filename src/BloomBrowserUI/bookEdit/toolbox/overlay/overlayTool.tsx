/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./overlay.less";
import {
    getEditablePageBundleExports,
    getEditTabBundleExports
} from "../../js/bloomFrames";
import { BubbleManager, ITextColorInfo } from "../../js/bubbleManager";
import { BubbleSpec, TailSpec } from "comicaljs";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@mui/material/FormControl";
import Select from "@mui/material/Select";
import { Button, MenuItem, Typography } from "@mui/material";
import { useL10n } from "../../../react_components/l10nHooks";
import { Div, Span } from "../../../react_components/l10nComponents";
import InputLabel from "@mui/material/InputLabel";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import { ColorBar } from "./colorBar";
import { IColorInfo } from "../../../react_components/color-picking/colorSwatch";
import { IColorPickerDialogProps } from "../../../react_components/color-picking/colorPickerDialog";
import * as tinycolor from "tinycolor2";
import { showSignLanguageTool } from "../../js/bloomVideo";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
import { RequiresBloomEnterpriseOverlayWrapper } from "../../../react_components/requiresBloomEnterprise";
import { kOverlayToolId } from "../toolIds";
import {
    BloomPalette,
    getColorInfoFromSpecialNameOrColorString,
    getSpecialColorName,
    TextBackgroundColors,
    TextColorPalette
} from "../../../react_components/color-picking/bloomPalette";
import { OverlayKeyHints } from "./overlayKeyHints";
import { EnableAllImageEditing } from "../../js/bloomImages";
import {
    OverlayImageItem,
    OverlayItem,
    OverlayItemRegion,
    OverlayItemRow,
    OverlayTextItem
} from "./overlayItem";
import { isPageBloomGame } from "../dragActivity/dragActivityTool";

const OverlayToolControls: React.FunctionComponent = () => {
    const l10nPrefix = "ColorPicker.";
    type BubbleType = "text" | "image" | "video" | undefined;

    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [outlineColor, setOutlineColor] = useState<string | undefined>(
        undefined
    );
    const [bubbleType, setBubbleType] = useState<BubbleType>(undefined);
    const [showTailChecked, setShowTailChecked] = useState(false);
    const [isRoundedCornersChecked, setIsRoundedCornersChecked] = useState(
        false
    );
    const [isXmatter, setIsXmatter] = useState(true);
    // This 'counter' increments on new page ready so we can re-check if the book is locked.
    const [pageRefreshIndicator, setPageRefreshIndicator] = useState(0);

    // Calls to useL10n
    const deleteTooltip = useL10n("Delete", "Common.Delete");
    const duplicateTooltip = useL10n(
        "Duplicate",
        "EditTab.Toolbox.ComicTool.Options.Duplicate"
    );

    // While renaming Comic -> Overlay, I (gjm) intentionally left several (21) "keys" with
    // the old "ComicTool" to avoid the whole deprecate/invalidate/retranslate issue.

    // Setup for color picker, in case we need it.
    const textColorTitle = useL10n(
        "Text Color",
        "EditTab.Toolbox.ComicTool.Options.TextColor"
    );
    const backgroundColorTitle = useL10n(
        "Background Color",
        "EditTab.Toolbox.ComicTool.Options.BackgroundColor"
    );

    const defaultTextColors: IColorInfo[] = TextColorPalette.map(color =>
        getColorInfoFromSpecialNameOrColorString(color)
    );

    // Text color swatch
    // defaults to "black" text color
    const [textColorSwatch, setTextColorSwatch] = useState(
        defaultTextColors[0]
    );
    const [textColorIsDefault, setTextColorIsDefault] = useState(true);

    // Background color swatch
    // defaults to "white" background color
    const [backgroundColorSwatch, setBackgroundColorSwatch] = useState(
        TextBackgroundColors[1]
    );

    // If bubbleType is not undefined, corresponds to the active bubble's family.
    // Otherwise, corresponds to the most recently active bubble's family.
    const [currentFamilySpec, setCurrentFamilySpec] = useState<
        BubbleSpec | undefined
    >(undefined);

    // Callback to initialize bubbleEditing and get the initial bubbleSpec
    const bubbleSpecInitialization = () => {
        const bubbleManager = OverlayTool.bubbleManager();
        if (!bubbleManager) {
            console.assert(
                false,
                "ERROR: Bubble manager is not initialized yet. Please investigate!"
            );
            return;
        }

        bubbleManager.turnOnBubbleEditing();
        bubbleManager.turnOnHidingImageButtons();
        bubbleManager.deselectVideoContainers();

        const bubbleSpec = bubbleManager.getSelectedFamilySpec();

        // The callback function is (currently) called when switching between bubbles, but is not called
        // if the tail spec changes, or for style and similar changes to the bubble that are initiated by React.
        bubbleManager.requestBubbleChangeNotification(
            "overlay",
            (bubble: BubbleSpec | undefined) => {
                setCurrentFamilySpec(bubble);
            }
        );

        setCurrentFamilySpec(bubbleSpec);
    };

    // Enhance: if we don't want to have a static, or don't want
    // this function to know about OverlayTool, we could just pass
    // a setter for this as a property.
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    OverlayTool.theOneOverlayTool!.callOnNewPageReady = () => {
        bubbleSpecInitialization();
        setIsXmatter(ToolboxToolReactAdaptor.isXmatter());
        const count = pageRefreshIndicator;
        setPageRefreshIndicator(count + 1);
    };

    // Reset UI when current bubble spec changes (e.g. user clicked on a bubble).
    useEffect(() => {
        if (currentFamilySpec) {
            setStyle(currentFamilySpec.style);
            setShowTailChecked(
                currentFamilySpec.tails && currentFamilySpec.tails.length > 0
            );
            setIsRoundedCornersChecked(
                !!currentFamilySpec.cornerRadiusX &&
                    !!currentFamilySpec.cornerRadiusY &&
                    currentFamilySpec.cornerRadiusX > 0 &&
                    currentFamilySpec.cornerRadiusY > 0
            );
            setOutlineColor(currentFamilySpec.outerBorderColor);
            const backColor = getBackgroundColorValue(currentFamilySpec);
            const newSwatch = getColorInfoFromSpecialNameOrColorString(
                backColor
            );
            setBackgroundColorSwatch(newSwatch);

            const bubbleMgr = OverlayTool.bubbleManager();
            setBubbleType(getBubbleType(bubbleMgr));
            if (bubbleMgr) {
                // Get the current bubble's textColor and set it
                const bubbleTextColorInformation: ITextColorInfo = bubbleMgr.getTextColorInformation();
                setTextColorIsDefault(bubbleTextColorInformation.isDefault);
                const newSwatch = getColorInfoFromSpecialNameOrColorString(
                    bubbleTextColorInformation.color
                );
                setTextColorSwatch(newSwatch);
            }
        } else {
            setBubbleType(undefined);
        }
    }, [currentFamilySpec]);

    const getBubbleType = (mgr: BubbleManager | undefined): BubbleType => {
        if (!mgr) {
            return undefined;
        }
        if (mgr.isActiveElementPictureOverPicture()) {
            return "image";
        }
        return mgr.isActiveElementVideoOverPicture() ? "video" : "text";
    };

    // Callback for style changed
    const handleStyleChanged = event => {
        const newStyle = event.target.value;

        // Update the toolbox controls
        setStyle(newStyle);

        // Update the Comical canvas on the page frame
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            const newBubbleProps = {
                style: newStyle
            };

            // BL-8537: If we are choosing "caption" style, we make sure that the background color is opaque.
            const backgroundColorArray = currentFamilySpec?.backgroundColors;
            if (
                newStyle === "caption" &&
                backgroundColorArray &&
                backgroundColorArray.length === 1
            ) {
                backgroundColorArray[0] = setOpaque(backgroundColorArray[0]);
            }

            // Avoid setting backgroundColorArray if it's just undefined.
            // Setting it to be undefined defines it as a property. This means that when objects are merged,
            // this object is considered to have a backgroundColors property, even though it may not be visible via JSON.stringify and even though
            // you may not have intended for it to overwrite prior values.
            if (backgroundColorArray !== undefined) {
                newBubbleProps["backgroundColors"] = backgroundColorArray;
            }

            const newSpec = bubbleMgr.updateSelectedFamilyBubbleSpec(
                newBubbleProps
            );
            // We do this because the new style's spec may affect Show Tail, or background opacity too.
            setCurrentFamilySpec(newSpec);
        }
    };

    // Callback for show tail checkbox changed
    // Presently, only disabled if style is "none".
    const handleShowTailChanged = (value: boolean) => {
        setShowTailChecked(value);

        // Update the Comical canvas on the page frame
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            bubbleMgr.updateSelectedFamilyBubbleSpec({
                tails: value ? [bubbleMgr.getDefaultTailSpec() as TailSpec] : []
            });
        }
    };

    // Callback for rounded corners checkbox changed
    const handleRoundedCornersChanged = (newValue: boolean | undefined) => {
        setIsRoundedCornersChecked(newValue || false);

        // Update the Comical canvas on the page frame
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            const radius = newValue ? 8 : undefined; // 8 is semi-arbitrary for now. We may add a control in the future to set it.
            bubbleMgr.updateSelectedFamilyBubbleSpec({
                cornerRadiusX: radius,
                cornerRadiusY: radius
            });
        }
    };

    const getBackgroundColorValue = (familySpec: BubbleSpec): string => {
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            const backgroundColorArray = bubbleMgr.getBackgroundColorArray(
                familySpec
            );
            if (backgroundColorArray.length === 1) {
                return backgroundColorArray[0]; // This could be a hex string or an rgba() string
            }
            const specialName = getSpecialColorName(backgroundColorArray);
            return specialName ? specialName : "white"; // maybe from a later version of Bloom? All we can do.
        } else {
            return "white";
        }
    };

    // We come into this from chooser change
    const updateTextColor = (newColor: IColorInfo) => {
        const color = newColor.colors[0]; // text color is always monochrome
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            // Update the toolbox controls
            setTextColorSwatch(newColor);

            bubbleMgr.setTextColor(color);
            // BL-9936/11104 Without this, bubble manager is up-to-date, but React doesn't know about it.
            updateReactFromComical(bubbleMgr);
        }
    };

    const defaultTextColorClicked = () => {
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            setTextColorIsDefault(true);
            bubbleMgr.setTextColor(""); // sets bubble to use style default
            updateReactFromComical(bubbleMgr);
        }
    };

    const noteInputFocused = (input: HTMLElement) =>
        OverlayTool.bubbleManager()?.setThingToFocusAfterSettingColor(input);

    // We come into this from chooser change
    const updateBackgroundColor = (newColor: IColorInfo) => {
        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            // Update the toolbox controls
            setBackgroundColorSwatch(newColor);

            // Update the Comical canvas on the page frame
            const backgroundColors = newColor.colors;
            bubbleMgr.setBackgroundColor(backgroundColors, newColor.opacity);
            // BL-9936/11104 Without this, bubble manager is up-to-date, but React doesn't know about it.
            updateReactFromComical(bubbleMgr);
        }
    };

    // We use this to get React's 'currentFamilySpec' up-to-date with what comical has, since some minor
    // React-initiated changes don't trigger BubbleManager's 'requestBubbleChangeNotification'.
    // Changing 'currentFamilySpec' is what updates the UI of the toolbox in general.
    const updateReactFromComical = (bubbleMgr: BubbleManager) => {
        const newSpec = bubbleMgr.getSelectedFamilySpec();
        setCurrentFamilySpec(newSpec);
    };

    // Callback when outline color of the bubble is changed
    const handleOutlineColorChanged = event => {
        let newValue = event.target.value;

        if (newValue === "none") {
            newValue = undefined;
        }

        const bubbleMgr = OverlayTool.bubbleManager();
        if (bubbleMgr) {
            // Update the toolbox controls
            setOutlineColor(newValue);

            // Update the Comical canvas on the page frame
            bubbleMgr.updateSelectedFamilyBubbleSpec({
                outerBorderColor: newValue
            });
        }
    };

    const styleSupportsRoundedCorners = (
        currentBubbleSpec: BubbleSpec | undefined
    ) => {
        if (!currentBubbleSpec) {
            return false;
        }

        const bgColors = currentBubbleSpec.backgroundColors;
        if (bgColors && bgColors.includes("transparent")) {
            // Don't allow on transparent bubbles
            return false;
        }

        switch (currentBubbleSpec.style) {
            case "caption":
                return true;
            case "none":
                // Just text - rounded corners applicable if it has a background color
                return bgColors && bgColors.length > 0;
            default:
                return false;
        }
    };

    const launchTextColorChooser = () => {
        const colorPickerDialogProps: IColorPickerDialogProps = {
            transparency: false,
            noGradientSwatches: true,
            localizedTitle: textColorTitle,
            initialColor: textColorSwatch,
            palette: BloomPalette.Text,
            isForOverlay: true,
            onChange: color => updateTextColor(color),
            onInputFocus: noteInputFocused,
            includeDefault: true,
            onDefaultClick: defaultTextColorClicked
            //defaultColor???
        };
        getEditTabBundleExports().showColorPickerDialog(colorPickerDialogProps);
    };

    // The background color chooser uses an alpha slider for transparency.
    // Unfortunately, with an alpha slider, the hex input will automatically switch to rgb
    // the moment the user sets alpha to anything but max opacity.
    const launchBackgroundColorChooser = (transparency: boolean) => {
        const colorPickerDialogProps: IColorPickerDialogProps = {
            transparency: transparency,
            localizedTitle: backgroundColorTitle,
            initialColor: backgroundColorSwatch,
            palette: BloomPalette.TextBackground,
            isForOverlay: true,
            onChange: color => updateBackgroundColor(color),
            onInputFocus: noteInputFocused
        };
        // If the background color is fully transparent, change it to fully opaque
        // so that the user can choose a color immediately (and adjust opacity to
        // a lower value as well if wanted).
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-9922.
        if (colorPickerDialogProps.initialColor.opacity === 0)
            colorPickerDialogProps.initialColor.opacity = 100;
        getEditTabBundleExports().showColorPickerDialog(colorPickerDialogProps);
    };

    const needToCalculateTransparency = (): boolean => {
        const opacityDecimal = backgroundColorSwatch.opacity;
        return opacityDecimal < 1.0;
    };

    const percentTransparentFromOpacity = !needToCalculateTransparency()
        ? "0" // We shouldn't call this under these circumstances.
        : (100 - (backgroundColorSwatch.opacity as number) * 100).toFixed(0);

    const transparencyString = useL10n(
        "Percent Transparent",
        l10nPrefix + "PercentTransparent",
        "",
        percentTransparentFromOpacity
    );

    // We need to calculate this (even though we may not need to display it) to keep from violating
    // React's rule about not changing the number of hooks rendered.
    // This is even more important now that we don't show this part of the UI sometimes (BL-9976)!
    const percentTransparencyString =
        percentTransparentFromOpacity === "0" ? undefined : transparencyString;

    // Note: Make sure bubble spec is the current ITEM's spec, not the current FAMILY's spec.
    const isChild = (bubbleSpec: BubbleSpec | undefined) => {
        const order = bubbleSpec?.order ?? 0;
        return order > 1;
    };

    const bubbleManager = OverlayTool.bubbleManager();
    const currentItemSpec = bubbleManager?.getSelectedItemBubbleSpec();

    // BL-8537 Because of the black shadow background, partly transparent backgrounds don't work for
    // captions. We'll use this to tell the color chooser not to show the alpha option.
    const isCaption = currentFamilySpec?.style === "caption";

    const getControlOptionsRegion = (): JSX.Element => {
        switch (bubbleType) {
            case "image":
                return (
                    <div id="videoOrImageSubstituteSection">
                        <Typography
                            css={css`
                                // "!important" is needed to keep .MuiTypography-root from overriding
                                margin: 15px 15px 0 15px !important;
                                text-align: center;
                            `}
                        >
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.ImageSelected">
                                There are no options for this kind of overlay
                            </Span>
                        </Typography>
                    </div>
                );
            case "video":
                return (
                    <div id="videoOrImageSubstituteSection">
                        <Button
                            css={css`
                                // Had to add "!important"s because .MuiButton-contained overrode them!
                                background-color: ${kBloomBlue} !important;
                                text-align: center;
                                margin: 20px 10px 5px 10px !important;
                                padding: 5px 0 !important; // defeat huge 'contained' style padding-right
                            `}
                            onClick={showSignLanguageTool}
                            size="large"
                            variant="contained"
                        >
                            <Typography
                                css={css`
                                    color: white;
                                `}
                            >
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.ShowSignLanguageTool">
                                    Show Sign Language Tool
                                </Span>
                            </Typography>
                        </Button>
                    </div>
                );
            case undefined:
            case "text":
                return (
                    <form autoComplete="off">
                        <FormControl variant="standard">
                            <InputLabel htmlFor="bubble-style-dropdown">
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.Style">
                                    Style
                                </Span>
                            </InputLabel>
                            <Select
                                variant="standard"
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
                                <MenuItem value="circle">
                                    <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Circle">
                                        Circle
                                    </Div>
                                </MenuItem>
                                <MenuItem value="rectangle">
                                    <Div l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Rectangle">
                                        Rectangle
                                    </Div>
                                </MenuItem>
                            </Select>

                            <BloomCheckbox
                                label="Show Tail"
                                l10nKey="EditTab.Toolbox.ComicTool.Options.ShowTail"
                                checked={showTailChecked}
                                disabled={isChild(currentItemSpec)}
                                onCheckChanged={v => {
                                    handleShowTailChanged(v as boolean);
                                }}
                            />

                            <BloomCheckbox
                                label="Rounded Corners"
                                l10nKey="EditTab.Toolbox.ComicTool.Options.RoundedCorners"
                                checked={isRoundedCornersChecked}
                                disabled={
                                    !styleSupportsRoundedCorners(
                                        currentFamilySpec
                                    )
                                }
                                onCheckChanged={newValue => {
                                    handleRoundedCornersChanged(newValue);
                                }}
                            />
                        </FormControl>
                        <FormControl variant="standard">
                            <InputLabel htmlFor="text-color-bar" shrink={true}>
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.TextColor">
                                    Text Color
                                </Span>
                            </InputLabel>
                            <ColorBar
                                id="text-color-bar"
                                onClick={launchTextColorChooser}
                                colorInfo={textColorSwatch}
                                isDefault={textColorIsDefault}
                            />
                        </FormControl>
                        <FormControl variant="standard">
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
                                onClick={() =>
                                    launchBackgroundColorChooser(!isCaption)
                                }
                                colorInfo={backgroundColorSwatch}
                                text={percentTransparencyString}
                            />
                        </FormControl>
                        <FormControl
                            variant="standard"
                            className={
                                isBubble(currentFamilySpec) ? "" : "disabled"
                            }
                        >
                            <InputLabel htmlFor="bubble-outlineColor-dropdown">
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor">
                                    Outer Outline Color
                                </Span>
                            </InputLabel>
                            <Select
                                variant="standard"
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
                                    if (isBubble(currentFamilySpec))
                                        handleOutlineColorChanged(event);
                                }}
                            >
                                <MenuItem value="none">
                                    <Div l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor.None">
                                        None
                                    </Div>
                                </MenuItem>
                                <MenuItem value="yellow">
                                    <Div l10nKey="Common.Colors.Yellow">
                                        Yellow
                                    </Div>
                                </MenuItem>
                                <MenuItem value="crimson">
                                    <Div l10nKey="Common.Colors.Crimson">
                                        Crimson
                                    </Div>
                                </MenuItem>
                            </Select>
                        </FormControl>
                    </form>
                );
        }
    };

    return (
        <div id="overlayToolControls">
            <RequiresBloomEnterpriseOverlayWrapper>
                {// Using most kinds of comic bubbles is problematic in various ways in Bloom games, so we don't allow it.
                // We may eventually want to allow some controls to be used, but for now we just disable the whole thing.
                // If we don't change our minds this string should get localized.
                // issues:
                // - making any kind of comic bubble that has a border, tail, etc able to be dragged in Play mode would
                // required Comical to be integrated into Bloom PLayer. I think even some things that don't seem to need
                // Comical, like setting a background color, are currently implemented using it.
                // - consequently it's a problem to enable any controls that would switch a play-time draggable to be
                // a bubble type whose rendering depends on Comical.
                // - it's also something of a problem to have fixed bubbles, since the parts rendered by Comical don't
                // obey the classes we use to dim things in Correct and Wrong tabs.
                // - if we allow Comical bubbles to be put in the Correct or Wrong tabs, the bit rendered by Comical
                // has to also get hidden until wanted in Start and Play modes.
                // - the duplicate command needs enhancements to do things like duplicating the target.
                // Enhance: if the practice of disabling some tools for some page types becomes widespread, we should
                // generalize this somehow, so we can easily configure which tools are disabled for which page types.
                // Without more examples, it's hard to know what would work best. For example, a page might have a data
                // attribute that lists ids of tools that should be disabled. Or somewhere we might have a map from
                // data-activity values to lists of tool ids that should be disabled. Or if disabling tools isn't
                // limited to activities, we might introduce a new kind of page type attribute. Also, we may just
                // want a single message like "This tool is not available on this page type" or something more specific
                // like the one here.
                isPageBloomGame() ? (
                    <Typography
                        css={css`
                            // "!important" is needed to keep .MuiTypography-root from overriding
                            margin: 15px 15px 0 15px !important;
                            text-align: center;
                        `}
                    >
                        <span>
                            The Overlay Tool cannot currently be used on Bloom
                            Games pages. Some of the functions are duplicated in
                            the Games Tool.
                        </span>
                    </Typography>
                ) : (
                    <div
                        css={css`
                            // pushes the Help region to the bottom
                            display: flex;
                            flex-direction: column;
                            height: 100%;
                        `}
                    >
                        <OverlayItemRegion
                            className={!isXmatter ? "" : "disabled"}
                        >
                            <OverlayItemRow>
                                <OverlayItem
                                    src="/bloom/bookEdit/toolbox/overlay/comic-icon.svg"
                                    style="speech"
                                />
                                <OverlayImageItem style="image" />
                                <OverlayItem
                                    src="/bloom/bookEdit/toolbox/overlay/sign-language-overlay.svg"
                                    style="video"
                                />
                            </OverlayItemRow>
                            <OverlayItemRow secondRow={true}>
                                <OverlayTextItem
                                    css={css`
                                        margin-left: 5px; // Match the spacing on the bubble icon above
                                        flex-grow: 1; // Let it fill as much space as possible to the right
                                        text-align: center; // Center the text horizontally

                                        padding-top: 1em;
                                        vertical-align: middle;
                                        padding-bottom: 1em;

                                        color: white;
                                        border: 1px dotted white;
                                    `}
                                    l10nKey="EditTab.Toolbox.ComicTool.TextBlock"
                                    style="none"
                                />

                                <OverlayTextItem
                                    css={css`
                                        // Horizontal positioning / sizing of the element
                                        margin-left: 10px;
                                        flex-grow: 1; // Allow it to fill the entire space (but with margin-left and margin-right outside of it)
                                        text-align: center;

                                        // Vertical sizing
                                        padding-top: 5px;
                                        padding-bottom: 5px;

                                        border: 1px solid black;
                                        color: black;
                                        background-color: white;
                                        box-shadow: 3px 3px black;
                                    `}
                                    l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Caption"
                                    style="caption"
                                />
                            </OverlayItemRow>
                        </OverlayItemRegion>

                        <div
                            id={"overlayToolControlOptionsRegion"}
                            className={
                                bubbleType && !isXmatter ? "" : "disabled"
                            }
                        >
                            {getControlOptionsRegion()}
                        </div>
                        <div id="overlayToolControlFillerRegion" />
                        <div id={"overlayToolControlFooterRegion"}>
                            <OverlayKeyHints />
                            <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Overlay_Tool/Overlay_Tool_overview.htm" />
                        </div>
                    </div>
                )}
            </RequiresBloomEnterpriseOverlayWrapper>
        </div>
    );
};
export default OverlayToolControls;

export class OverlayTool extends ToolboxToolReactAdaptor {
    public static theOneOverlayTool: OverlayTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        OverlayTool.theOneOverlayTool = this;
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "OverlayBody");

        ReactDOM.render(<OverlayToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return kOverlayToolId;
    }

    public isExperimental(): boolean {
        return false;
    }

    public toolRequiresEnterprise(): boolean {
        return true;
    }

    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    public newPageReady() {
        const bubbleManager = OverlayTool.bubbleManager();
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
        const bubbleManager = OverlayTool.bubbleManager();
        if (bubbleManager) {
            // For now we are leaving bubble editing on, because even with the toolbox hidden,
            // the user might edit text, delete bubbles, move handles, etc.
            // We turn it off only when about to save the page.
            //bubbleManager.turnOffBubbleEditing();

            EnableAllImageEditing();
            bubbleManager.detachBubbleChangeNotification("overlay");
        }
    }

    public static bubbleManager(): BubbleManager | undefined {
        const exports = getEditablePageBundleExports();
        return exports ? exports.getTheOneBubbleManager() : undefined;
    }
}

function setOpaque(color: string) {
    const firstColor = new tinycolor(color);
    firstColor.setAlpha(1.0);
    return firstColor.toHexString();
}
function isBubble(item: BubbleSpec | undefined): boolean {
    // "none" is the style assigned to the plain text box.
    return !!item && item.style != "none" && item.style != "caption";
}
