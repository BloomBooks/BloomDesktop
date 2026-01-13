import { css, ThemeProvider } from "@emotion/react";

import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./canvasTool.less";
import { getEditTabBundleExports } from "../../js/bloomFrames";
import {
    CanvasElementManager,
    ITextColorInfo,
} from "../../js/CanvasElementManager";
import { Bubble, BubbleSpec, TailSpec } from "comicaljs";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@mui/material/FormControl";
import Select from "@mui/material/Select";
import { MenuItem, Typography } from "@mui/material";
import { useL10n } from "../../../react_components/l10nHooks";
import { Div, Span } from "../../../react_components/l10nComponents";
import InputLabel from "@mui/material/InputLabel";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import { ColorBar } from "./colorBar";
import { IColorInfo } from "../../../react_components/color-picking/colorSwatch";
import { IColorPickerDialogProps } from "../../../react_components/color-picking/colorPickerDialog";
import tinycolor from "tinycolor2";
import { RequiresSubscriptionOverlayWrapper } from "../../../react_components/requiresSubscription";
import { kCanvasToolId } from "../toolIds";
import {
    BloomPalette,
    getColorInfoFromSpecialNameOrColorString,
    getSpecialColorName,
    TextBackgroundColors,
    TextColorPalette,
} from "../../../react_components/color-picking/bloomPalette";
import { EnableAllImageEditing } from "../../js/bloomImages";
import {
    CanvasElementCaptionItem,
    CanvasElementImageItem,
    CanvasElementItem,
    CanvasElementItemRegion,
    CanvasElementItemRow,
    CanvasElementLinkGridItem,
    NavigationImageButtonPaletteItem,
    CanvasElementTextItem,
    NavigationLabelButtonPaletteItem,
    NavigationImageWithLabelButtonPaletteItem,
} from "./CanvasElementItem";
import {
    getCanvasElementManager,
    kBloomButtonClass,
} from "./canvasElementUtils";
import { deselectVideoContainers } from "../../js/videoUtils";
import { CanvasElementKeyHints } from "./CanvasElementKeyHints";
import { ToolBox } from "../toolbox";
import {
    kBloomBlue,
    kToolboxContentPadding,
    toolboxMenuPopupTheme,
    toolboxTheme,
} from "../../../bloomMaterialUITheme";
import $ from "jquery";
import { TriangleCollapse } from "../../../react_components/TriangleCollapse";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { text } from "stream/consumers";

const CanvasToolControls: React.FunctionComponent = () => {
    const l10nPrefix = "ColorPicker.";
    type CanvasElementType = "text" | "image" | "video" | undefined;

    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [outlineColor, setOutlineColor] = useState<string | undefined>(
        undefined,
    );
    const [canvasElementType, setCanvasElementType] =
        useState<CanvasElementType>(undefined);
    const [showTailChecked, setShowTailChecked] = useState(false);
    const [isRoundedCornersChecked, setIsRoundedCornersChecked] =
        useState(false);
    const [isXmatter, setIsXmatter] = useState(true);
    // This 'counter' increments on new page ready so we can re-check if the book is locked.
    const [pageRefreshIndicator, setPageRefreshIndicator] = useState(0);

    // Add state to track whether each dropdown is open
    const [isStyleSelectOpen, setIsStyleSelectOpen] = useState(false);
    const [isOutlineColorSelectOpen, setIsOutlineColorSelectOpen] =
        useState(false);
    function openStyleSelect() {
        setIsStyleSelectOpen(true);
        // Make sure we don't leave the select open when the tool closes.
        ToolBox.addWhenClosingToolTask(() => {
            setIsStyleSelectOpen(false);
        });
        window.addEventListener(
            "blur",
            () => {
                setIsStyleSelectOpen(false);
            },
            { once: true },
        );
    }
    function openOutlineColorSelect() {
        setIsOutlineColorSelectOpen(true);
        // Make sure we don't leave the select open when the tool closes.
        ToolBox.addWhenClosingToolTask(() => {
            setIsOutlineColorSelectOpen(false);
        });
        window.addEventListener(
            "blur",
            () => {
                setIsOutlineColorSelectOpen(false);
            },
            { once: true },
        );
    }

    // Calls to useL10n
    const deleteTooltip = useL10n("Delete", "Common.Delete");
    const duplicateTooltip = useL10n(
        "Duplicate",
        "EditTab.Toolbox.ComicTool.Options.Duplicate",
    );

    // While renaming Comic -> Overlay, I (gjm) intentionally left several (21) "keys" with
    // the old "ComicTool" to avoid the whole deprecate/invalidate/retranslate issue.
    // When renaming "Overlay" to "Canvas", we still kept those same keys.

    // Setup for color picker, in case we need it.
    const textColorTitle = useL10n(
        "Text Color",
        "EditTab.Toolbox.ComicTool.Options.TextColor",
    );
    const backgroundColorTitle = useL10n(
        "Background Color",
        "EditTab.Toolbox.ComicTool.Options.BackgroundColor",
    );

    const defaultTextColors: IColorInfo[] = TextColorPalette.map((color) =>
        getColorInfoFromSpecialNameOrColorString(color),
    );

    // Text color swatch
    // defaults to "black" text color
    const [textColorSwatch, setTextColorSwatch] = useState(
        defaultTextColors[0],
    );
    const [textColorIsDefault, setTextColorIsDefault] = useState(true);

    // Background color swatch
    // defaults to "white" background color
    const [backgroundColorSwatch, setBackgroundColorSwatch] = useState(
        TextBackgroundColors[1],
    );

    // If canvasElementType is not undefined, corresponds to the active canvas element's family.
    // Otherwise, corresponds to the most recently active canvas element's family.
    const [currentBubble, setCurrentBubble] = useState<Bubble | undefined>(
        undefined,
    );

    // Callback to initialize bubbleEditing and get the initial bubbleSpec
    const bubbleSpecInitialization = () => {
        const canvasElementManager = getCanvasElementManager();
        if (!canvasElementManager) {
            console.assert(
                false,
                "ERROR: Bubble manager is not initialized yet. Please investigate!",
            );
            return;
        }

        canvasElementManager.turnOnCanvasElementEditing();
        deselectVideoContainers();

        const bubble = canvasElementManager.getPatriarchBubbleOfActiveElement();

        // The callback function is (currently) called when switching between canvas elements, but is not called
        // if the tail spec changes, or for style and similar changes to the canvas element that are initiated by React.
        canvasElementManager.requestCanvasElementChangeNotification(
            "canvasElement",
            (bubble: Bubble | undefined) => {
                setCurrentBubble(bubble);
            },
        );

        setCurrentBubble(bubble);
    };

    // Enhance: if we don't want to have a static, or don't want
    // this function to know about CanvasTool, we could just pass
    // a setter for this as a property.

    CanvasTool.theOneCanvasTool!.callOnNewPageReady = () => {
        bubbleSpecInitialization();
        setIsXmatter(ToolboxToolReactAdaptor.isXmatter());
        const count = pageRefreshIndicator;
        setPageRefreshIndicator(count + 1);
    };

    // Reset UI when current bubble spec changes (e.g. user clicked on a bubble).
    useEffect(() => {
        if (currentBubble) {
            const currentBubbleSpec = currentBubble.getBubbleSpec();
            setStyle(currentBubbleSpec.style);
            setShowTailChecked(
                currentBubbleSpec.tails && currentBubbleSpec.tails.length > 0,
            );
            setIsRoundedCornersChecked(
                !!currentBubbleSpec.cornerRadiusX &&
                    !!currentBubbleSpec.cornerRadiusY &&
                    currentBubbleSpec.cornerRadiusX > 0 &&
                    currentBubbleSpec.cornerRadiusY > 0,
            );
            setOutlineColor(currentBubbleSpec.outerBorderColor);
            const isButton =
                currentBubble.content.classList.contains(kBloomButtonClass);
            const backColor = isButton
                ? getBackgroundColorValueForButton(currentBubble.content)
                : getBackgroundColorValue(currentBubbleSpec);
            const newSwatch =
                getColorInfoFromSpecialNameOrColorString(backColor);
            setBackgroundColorSwatch(newSwatch);

            const canvasElementManager = getCanvasElementManager();
            setCanvasElementType(getBubbleType(canvasElementManager));
            if (canvasElementManager) {
                // Get the current canvas element's textColor and set it
                const canvasElementTextColorInformation: ITextColorInfo =
                    canvasElementManager.getTextColorInformation();
                setTextColorIsDefault(
                    canvasElementTextColorInformation.isDefault,
                );
                const newSwatch = getColorInfoFromSpecialNameOrColorString(
                    canvasElementTextColorInformation.color,
                );
                setTextColorSwatch(newSwatch);
            }
        } else {
            setCanvasElementType(undefined);
        }
    }, [currentBubble]);

    const getBubbleType = (
        mgr: CanvasElementManager | undefined,
    ): CanvasElementType => {
        if (!mgr) {
            return undefined;
        }
        if (mgr.isActiveElementPictureCanvasElement()) {
            return "image";
        }
        return mgr.isActiveElementVideoCanvasElement() ? "video" : "text";
    };

    // Callback for style changed
    const handleStyleChanged = (event) => {
        const newStyle = event.target.value;

        // Update the toolbox controls
        setStyle(newStyle);

        // Update the Comical canvas on the page frame
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            const newBubbleProps = {
                style: newStyle,
            };

            // BL-8537: If we are choosing "caption" style, we make sure that the background color is opaque.
            const backgroundColorArray =
                currentBubble?.getBubbleSpec()?.backgroundColors;
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

            const bubble =
                canvasElementManager.updateSelectedFamilyBubbleSpec(
                    newBubbleProps,
                );
            // We do this because the new style's spec may affect Show Tail, or background opacity too.
            setCurrentBubble(bubble);
        }
    };

    // Callback for show tail checkbox changed
    // Presently, only disabled if style is "none".
    const handleShowTailChanged = (value: boolean) => {
        setShowTailChecked(value);

        // Update the Comical canvas on the page frame
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            canvasElementManager.updateSelectedFamilyBubbleSpec({
                tails: value
                    ? [canvasElementManager.getDefaultTailSpec() as TailSpec]
                    : [],
            });
        }
    };

    // Callback for rounded corners checkbox changed
    const handleRoundedCornersChanged = (newValue: boolean | undefined) => {
        setIsRoundedCornersChecked(newValue || false);

        // Update the Comical canvas on the page frame
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            const radius = newValue ? 8 : undefined; // 8 is semi-arbitrary for now. We may add a control in the future to set it.
            canvasElementManager.updateSelectedFamilyBubbleSpec({
                cornerRadiusX: radius,
                cornerRadiusY: radius,
            });
        }
    };

    const getBackgroundColorValue = (familySpec: BubbleSpec): string => {
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            const backgroundColorArray =
                canvasElementManager.getBackgroundColorArray(familySpec);
            if (backgroundColorArray.length === 1) {
                return backgroundColorArray[0]; // This could be a hex string or an rgba() string
            }
            const specialName = getSpecialColorName(backgroundColorArray);
            return specialName ? specialName : "white"; // maybe from a later version of Bloom? All we can do.
        } else {
            return "white";
        }
    };

    // For buttons, a single BG color is stored in style.backgroundColor, and a gradient
    // as a linear-gradient in style.background. We return a single string even for gradients,
    // like getBackgroundColorValue, by only handling the special color names that we've decided
    // to support. I don't know why we decided on that limitation; it may have to do with
    // the fact that our color chooser is not powerful enough to handle gradients.
    // This code will need enhancing if we want to support gradients with a variety of colors
    // or any other direction than the default top-to-bottom.
    const getBackgroundColorValueForButton = (
        canvasElement: HTMLElement,
    ): string => {
        const bgColor = canvasElement.style.backgroundColor;
        // For some reason when it is empty, what we get here is "initial"
        if (bgColor && bgColor !== "initial") {
            return bgColor;
        }
        const bgGradient = canvasElement.style.background;
        if (bgGradient && bgGradient.startsWith("linear-gradient")) {
            // get the array of colors from the gradient
            const colors = bgGradient.substring(
                bgGradient.indexOf("(") + 1,
                bgGradient.lastIndexOf(")"),
            );
            // Parsing the linear-gradient is quite tricky because even though we write the colors
            // as hash values, they come back as rgb() strings, and then commas are used both
            // to separate the arguments to rgb() and to separate the colors in the gradient.
            // And then we have to convert back to hex to match our special color names.
            const colorArray: string[] = [];
            let currentToken = "";
            let parenDepth = 0;
            for (let i = 0; i < colors.length; i++) {
                const char = colors[i];
                if (char === "(") {
                    parenDepth++;
                    currentToken += char;
                } else if (char === ")") {
                    parenDepth--;
                    currentToken += char;
                } else if (char === "," && parenDepth === 0) {
                    colorArray.push(currentToken.trim());
                    currentToken = "";
                } else {
                    currentToken += char;
                }
            }
            if (currentToken) {
                colorArray.push(currentToken.trim());
            }
            // Convert any rgb() colors to hex format
            const hexColorArray = colorArray.map((color) => {
                if (color.startsWith("rgb(")) {
                    const tc = tinycolor(color);
                    return tc.isValid() ? tc.toHexString() : color;
                }
                return color;
            });
            const specialName = getSpecialColorName(hexColorArray);
            return specialName ? specialName : "white"; // maybe from a later version of Bloom? All we can do.
        }
        return "white"; // best we can do
    };

    // We come into this from chooser change
    const updateTextColor = (newColor: IColorInfo) => {
        const color = newColor.colors[0]; // text color is always monochrome
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            // Update the toolbox controls
            setTextColorSwatch(newColor);

            canvasElementManager.setTextColor(color);
            // BL-9936/11104 Without this, CanvasElementManager is up-to-date, but React doesn't know about it.
            updateReactFromComical(canvasElementManager);
        }
    };

    const defaultTextColorClicked = () => {
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            setTextColorIsDefault(true);
            canvasElementManager.setTextColor(""); // sets canvas element to use style default
            updateReactFromComical(canvasElementManager);
        }
    };

    const noteInputFocused = (input: HTMLElement) =>
        getCanvasElementManager()?.setThingToFocusAfterSettingColor(input);

    // We come into this from chooser change
    const updateBackgroundColor = (newColor: IColorInfo) => {
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            // Update the toolbox controls
            setBackgroundColorSwatch(newColor);

            // Update the Comical canvas on the page frame
            const backgroundColors = newColor.colors;
            canvasElementManager.setBackgroundColor(
                backgroundColors,
                newColor.opacity,
            );
            // BL-9936/11104 Without this, CanvasElementManager is up-to-date, but React doesn't know about it.
            updateReactFromComical(canvasElementManager);
        }
    };

    // We use this to get React's 'currentFamilySpec' up-to-date with what comical has, since some minor
    // React-initiated changes don't trigger CanvasElementManager's 'requestBubbleChangeNotification'.
    // Changing 'currentFamilySpec' is what updates the UI of the toolbox in general.
    const updateReactFromComical = (
        canvasElementManager: CanvasElementManager,
    ) => {
        const bubble = canvasElementManager.getPatriarchBubbleOfActiveElement();
        setCurrentBubble(bubble);
    };

    // Callback when outline color of the bubble is changed
    const handleOutlineColorChanged = (event) => {
        let newValue = event.target.value;

        if (newValue === "none") {
            newValue = undefined;
        }

        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            // Update the toolbox controls
            setOutlineColor(newValue);

            // Update the Comical canvas on the page frame
            canvasElementManager.updateSelectedFamilyBubbleSpec({
                outerBorderColor: newValue,
            });
        }
    };

    const styleSupportsRoundedCorners = (
        currentBubbleSpec: BubbleSpec | undefined,
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
            isForCanvasElement: true,
            onChange: (color) => updateTextColor(color),
            onInputFocus: noteInputFocused,
            includeDefault: true,
            onDefaultClick: defaultTextColorClicked,
            //defaultColor???
        };
        getEditTabBundleExports().showColorPickerDialog(colorPickerDialogProps);
        ToolBox.addWhenClosingToolTask(() => {
            getEditTabBundleExports().hideColorPickerDialog();
        });
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
            isForCanvasElement: true,
            onChange: (color) => updateBackgroundColor(color),
            onInputFocus: noteInputFocused,
        };
        // If the background color is fully transparent, change it to fully opaque
        // so that the user can choose a color immediately (and adjust opacity to
        // a lower value as well if wanted).
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-9922.
        if (colorPickerDialogProps.initialColor.opacity === 0)
            colorPickerDialogProps.initialColor.opacity = 100;
        getEditTabBundleExports().showColorPickerDialog(colorPickerDialogProps);
        ToolBox.addWhenClosingToolTask(() => {
            getEditTabBundleExports().hideColorPickerDialog();
        });
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
        percentTransparentFromOpacity,
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

    const canvasElementManager = getCanvasElementManager();
    const currentItemSpec = canvasElementManager?.getSelectedItemBubbleSpec();

    // BL-8537 Because of the black shadow background, partly transparent backgrounds don't work for
    // captions. We'll use this to tell the color chooser not to show the alpha option.
    const isCaption = currentBubble?.getBubbleSpec()?.style === "caption";

    const backgroundColorControl = (
        <FormControl variant="standard">
            <InputLabel shrink={true} htmlFor="background-color-bar">
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor">
                    Background Color
                </Span>
            </InputLabel>
            <ColorBar
                id="background-color-bar"
                onClick={() => launchBackgroundColorChooser(!isCaption)}
                colorInfo={backgroundColorSwatch}
                text={percentTransparencyString}
            />
        </FormControl>
    );
    const textColorControl = (
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
    );

    const activeElement = canvasElementManager?.getActiveElement();
    const isButton =
        activeElement?.classList.contains(kBloomButtonClass) ?? false;
    const hasText =
        (activeElement?.getElementsByClassName("bloom-translationGroup")
            ?.length ?? 0) > 0;
    const isBookGrid =
        (activeElement?.getElementsByClassName("bloom-link-grid")?.length ??
            0) > 0;

    const noControlsSection = (
        <div id="noOptionsSection">
            <Typography
                css={css`
                    // "!important" is needed to keep .MuiTypography-root from overriding
                    margin: 15px 15px 0 15px !important;
                    text-align: center;
                `}
            >
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.ImageSelected">
                    There are no options for this kind of canvas element
                </Span>
            </Typography>
        </div>
    );

    const getControlOptionsRegion = (): JSX.Element => {
        if (isBookGrid) return noControlsSection;
        if (isButton)
            return (
                <>
                    {hasText && textColorControl}
                    {backgroundColorControl}
                </>
            );
        switch (canvasElementType) {
            case "image":
            case "video":
                return noControlsSection;
            case undefined:
            case "text":
                return (
                    <form autoComplete="off">
                        <FormControl variant="standard">
                            <InputLabel htmlFor="canvasElement-style-dropdown">
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.Style">
                                    Style
                                </Span>
                            </InputLabel>
                            <ThemeProvider theme={toolboxMenuPopupTheme}>
                                <Select
                                    variant="standard"
                                    value={style}
                                    open={isStyleSelectOpen}
                                    onOpen={openStyleSelect}
                                    onClose={() => setIsStyleSelectOpen(false)}
                                    onChange={(event) => {
                                        handleStyleChanged(event);
                                        setIsStyleSelectOpen(false);
                                    }}
                                    className="canvasElementOptionDropdown"
                                    inputProps={{
                                        name: "style",
                                        id: "canvasElement-style-dropdown",
                                    }}
                                    MenuProps={{
                                        className:
                                            "canvasElement-options-dropdown-menu",
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
                            </ThemeProvider>

                            <BloomCheckbox
                                label="Show Tail"
                                l10nKey="EditTab.Toolbox.ComicTool.Options.ShowTail"
                                checked={showTailChecked}
                                disabled={isChild(currentItemSpec) || isButton}
                                onCheckChanged={(v) => {
                                    handleShowTailChanged(v as boolean);
                                }}
                            />

                            <BloomCheckbox
                                label="Rounded Corners"
                                l10nKey="EditTab.Toolbox.ComicTool.Options.RoundedCorners"
                                checked={isRoundedCornersChecked}
                                disabled={
                                    !styleSupportsRoundedCorners(
                                        currentBubble?.getBubbleSpec(),
                                    )
                                }
                                onCheckChanged={(newValue) => {
                                    handleRoundedCornersChanged(newValue);
                                }}
                            />
                        </FormControl>
                        {textColorControl}
                        {backgroundColorControl}
                        <FormControl
                            variant="standard"
                            className={
                                isBubble(currentBubble?.getBubbleSpec())
                                    ? ""
                                    : "disabled"
                            }
                        >
                            <InputLabel htmlFor="canvasElement-outlineColor-dropdown">
                                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor">
                                    Outer Outline Color
                                </Span>
                            </InputLabel>
                            <ThemeProvider theme={toolboxMenuPopupTheme}>
                                <Select
                                    variant="standard"
                                    value={outlineColor ? outlineColor : "none"}
                                    open={isOutlineColorSelectOpen}
                                    onOpen={openOutlineColorSelect}
                                    onClose={() =>
                                        setIsOutlineColorSelectOpen(false)
                                    }
                                    className="canvasElementOptionDropdown"
                                    inputProps={{
                                        name: "outlineColor",
                                        id: "canvasElement-outlineColor-dropdown",
                                    }}
                                    MenuProps={{
                                        className:
                                            "canvasElement-options-dropdown-menu",
                                    }}
                                    onChange={(event) => {
                                        if (
                                            isBubble(
                                                currentBubble?.getBubbleSpec(),
                                            )
                                        ) {
                                            handleOutlineColorChanged(event);
                                            setIsOutlineColorSelectOpen(false);
                                        }
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
                            </ThemeProvider>
                        </FormControl>
                    </form>
                );
        }
    };

    return (
        <ThemeProvider theme={toolboxTheme}>
            <RequiresSubscriptionOverlayWrapper
                featureName={kCanvasToolId as string}
            >
                <div id="canvasToolControls">
                    {
                        // Using most kinds of comic bubbles is problematic in various ways in Bloom games, so we don't allow it.
                        // We may eventually want to allow some controls to be used, but for now we just disable the whole thing.
                        // If we don't change our minds this string should get localized.
                        // issues:
                        // - making any kind of canvas element that has a border, tail, etc able to be dragged in Play mode would
                        // required Comical to be integrated into Bloom PLayer. I think even some things that don't seem to need
                        // Comical, like setting a background color, are currently implemented using it.
                        // - consequently it's a problem to enable any controls that would switch a play-time draggable to be
                        // a canvas element type whose rendering depends on Comical.
                        // - it's also something of a problem to have fixed canvas elements, since the parts rendered by Comical don't
                        // obey the classes we use to dim things in Correct and Wrong tabs.
                        // - if we allow Comical canvas elements to be put in the Correct or Wrong tabs, the bit rendered by Comical
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
                        CanvasTool.isCurrentPageABloomGame() ? (
                            <div
                                css={css`
                                    padding: ${kToolboxContentPadding};
                                `}
                            >
                                <Typography
                                    css={css`
                                        text-align: center;
                                    `}
                                >
                                    <span>
                                        The Canvas Tool cannot currently be used
                                        on Bloom Games pages. Some of the
                                        functions are duplicated in the Games
                                        Tool.
                                    </span>
                                </Typography>
                            </div>
                        ) : (
                            <div
                                css={css`
                                    // pushes the Help region to the bottom
                                    display: flex;
                                    flex-direction: column;
                                    height: 100%;
                                `}
                            >
                                <CanvasElementItemRegion
                                    theme="blueOnTan"
                                    className={!isXmatter ? "" : "disabled"}
                                >
                                    <CanvasElementItemRow>
                                        <CanvasElementItem
                                            src="/bloom/bookEdit/toolbox/canvas/comic-icon.svg"
                                            canvasElementType="speech"
                                        />
                                        <CanvasElementImageItem
                                            color={kBloomBlue}
                                            strokeColor={kBloomBlue}
                                        />
                                        <CanvasElementItem
                                            src="/bloom/bookEdit/toolbox/canvas/sign-language-overlay.svg"
                                            canvasElementType="video"
                                        />
                                    </CanvasElementItemRow>
                                    <CanvasElementItemRow secondRow={true}>
                                        <CanvasElementTextItem
                                            css={css`
                                                margin-left: 5px; // Match the spacing on the canvas element icon above
                                                flex-grow: 1; // Let it fill as much space as possible to the right
                                                text-align: center; // Center the text horizontally

                                                padding-top: 1em;
                                                vertical-align: middle;
                                                padding-bottom: 1em;

                                                color: ${kBloomBlue};
                                                background-color: white;
                                                border: 1px dotted ${kBloomBlue};
                                            `}
                                            l10nKey="EditTab.Toolbox.ComicTool.TextBlock"
                                        />

                                        <CanvasElementCaptionItem
                                            css={css`
                                                // Horizontal positioning / sizing of the element
                                                margin-left: 10px;
                                                flex-grow: 1; // Allow it to fill the entire space (but with margin-left and margin-right outside of it)
                                                text-align: center;

                                                // Vertical sizing
                                                padding-top: 5px;
                                                padding-bottom: 5px;

                                                border: 1px solid ${kBloomBlue};
                                                color: ${kBloomBlue};
                                                background-color: white;
                                                box-shadow: 3px 3px
                                                    ${kBloomBlue};
                                            `}
                                            l10nKey="EditTab.Toolbox.ComicTool.Options.Style.Caption"
                                        />
                                    </CanvasElementItemRow>

                                    <TriangleCollapse
                                        initiallyOpen={false}
                                        labelL10nKey="EditTab.Toolbox.CanvasTool.Navigation"
                                        helpId="Tasks/Edit_tasks/Canvas_Tool/Navigation_overview.htm"
                                        buttonColor="#1D94A4"
                                        css={css`
                                            margin-top: 0;
                                            padding: 0;
                                            font-size: 14px;
                                        `}
                                        extraButtonCss="padding: 0;"
                                    >
                                        <div>
                                            <BloomTooltip
                                                id="navButtons"
                                                placement="top-end"
                                                tip={
                                                    <Div l10nKey="EditTab.Toolbox.CanvasTool.NavButtons"></Div>
                                                }
                                            >
                                                <CanvasElementItemRow extraCss="margin-top:0; height: 80px;">
                                                    <>
                                                        <NavigationImageWithLabelButtonPaletteItem />
                                                        <NavigationImageButtonPaletteItem />
                                                        <NavigationLabelButtonPaletteItem />
                                                    </>
                                                </CanvasElementItemRow>
                                            </BloomTooltip>

                                            <CanvasElementItemRow extraCss="margin-top:0;">
                                                <BloomTooltip
                                                    id="navButtons"
                                                    placement="top-end"
                                                    tip={
                                                        <Div l10nKey="EditTab.Toolbox.CanvasTool.BookGrid"></Div>
                                                    }
                                                >
                                                    <CanvasElementLinkGridItem />
                                                </BloomTooltip>
                                            </CanvasElementItemRow>
                                        </div>
                                    </TriangleCollapse>
                                </CanvasElementItemRegion>
                                <div
                                    id={"canvasToolControlOptionsRegion"}
                                    className={
                                        canvasElementType && !isXmatter
                                            ? ""
                                            : "disabled"
                                    }
                                >
                                    {getControlOptionsRegion()}
                                </div>
                                <div id="canvasToolControlFillerRegion" />
                                <div id={"canvasToolControlFooterRegion"}>
                                    <CanvasElementKeyHints />
                                    <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Canvas_Tool/Canvas_Tool_overview.htm" />
                                </div>
                            </div>
                        )
                    }
                </div>
            </RequiresSubscriptionOverlayWrapper>
        </ThemeProvider>
    );
};
export default CanvasToolControls;

// Possibly wants to be CanvasElementTool, but we may think of a better UI name and want to use that instead, so leaving for now.
export class CanvasTool extends ToolboxToolReactAdaptor {
    public static theOneCanvasTool: CanvasTool | undefined;

    public callOnNewPageReady: () => void | undefined;

    public constructor() {
        super();

        CanvasTool.theOneCanvasTool = this;
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CanvasBody");

        ReactDOM.render(<CanvasToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return kCanvasToolId;
    }

    public featureName? = kCanvasToolId;

    public isExperimental(): boolean {
        return false;
    }

    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    public newPageReady() {
        const canvasElementManager = getCanvasElementManager();
        if (!canvasElementManager) {
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
                "CallOnNewPageReady is always expected to be defined but it is not.",
            );
        }
    }

    public detachFromPage() {
        const canvasElementManager = getCanvasElementManager();
        if (canvasElementManager) {
            // For now we are leaving canvas element editing on, because even with the toolbox hidden,
            // the user might edit text, delete canvas elements, move handles, etc.
            // We turn it off only when about to save the page.
            //CanvasElementManager.turnOffBubbleEditing();

            EnableAllImageEditing();
            canvasElementManager.detachCanvasElementChangeNotification(
                "canvasElement",
            );
        }
    }

    // In the process of moving this to a minimal-dependency utility file, but a lot of
    // code still expects to find it here.
    public static getCanvasElementManager(): CanvasElementManager | undefined {
        return getCanvasElementManager();
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
