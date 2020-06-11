import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./comic.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { BubbleManager } from "../../js/bubbleManager";
import { BubbleSpec, TailSpec } from "comicaljs";
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
import { MuiCheckbox } from "../../../react_components/muiCheckBox";
import { MenuColorBar } from "./menuColorBar";
import { ColorResult } from "react-color";
import { ISwatchDefn } from "../../../react_components/colorSwatch";
import CustomColorPicker, {
    getSwatchFromHex
} from "../../../react_components/customColorPicker";
import * as tinycolor from "tinycolor2";

export interface IMenuItem extends ISwatchDefn {
    l10nKey?: string; // if present, there should be default text in the (below) 'text' property to display
    l10nParam?: string;
    text?: string;
}

const ComicToolControls: React.FunctionComponent = () => {
    const maxChooserSwatches = 12;
    const maxMenuItems = 10;
    // In both cases these insertion indices reflect the size of the non-customized menu, less the "New..."
    const backgroundMenuCustomInsertionIndex = 6;
    const textMenuCustomInsertionIndex = 4;
    const specialColors: IMenuItem[] = [
        // #DFB28B is the color Comical has been using as the default for captions.
        // It's fairly close to the "Calico" color defined at https://www.htmlcsscolor.com/hex/D5B185 (#D5B185)
        // so I decided it was the best choice for keeping that option.
        {
            name: "whiteToCalico",
            colors: ["white", "#DFB28B"]
        },
        // https://www.htmlcsscolor.com/hex/ACCCDD
        {
            name: "whiteToFrenchPass",
            colors: ["white", "#ACCCDD"]
        },
        // https://encycolorpedia.com/7b8eb8
        {
            name: "whiteToPortafino",
            colors: ["white", "#7b8eb8"]
        }
    ];

    const l10nPrefix = "ColorPicker.";
    const newMenuItem: IMenuItem = {
        name: "new",
        l10nKey: l10nPrefix + "New",
        colors: ["white"],
        text: "New..."
    };

    const defaultTextColors: IMenuItem[] = [
        {
            name: "black",
            colors: ["black"]
        },
        {
            name: "gray",
            colors: ["gray"] // #808080
        },
        {
            name: "lightgray",
            colors: ["lightgray"] // #D3D3D3
        },
        { name: "white", colors: ["white"] }
    ];

    const defaultBackgroundColors: IMenuItem[] = [
        {
            name: "black",
            colors: ["black"]
        },
        { name: "white", colors: ["white"] },
        {
            name: "partialTransparent",
            colors: ["#575757"], // bloom-gray
            opacity: 0.5,
            l10nKey: l10nPrefix + "PercentTransparent",
            l10nParam: "50",
            text: "PercentTransparent"
        }
    ];

    // Declare all the hooks
    const [style, setStyle] = useState("none");
    const [outlineColor, setOutlineColor] = useState<string | undefined>(
        undefined
    );
    const [bubbleActive, setBubbleActive] = useState(false);
    const [showTailChecked, setShowTailChecked] = useState(false);

    const [isXmatter, setIsXmatter] = useState(true);

    // Text color menu and chooser
    const [textColor, setTextColor] = useState("black");
    //const [nextCustomText, setNextCustomText] = useState(1);
    const [textColorMenuItems, setTextColorMenuItems] = useState(
        defaultTextColors.concat(newMenuItem)
    );
    const [textChooserSwatches, setTextChooserSwatches] = useState(
        defaultTextColors
    );
    // defaults to "black" text color
    const [textColorMenuItem, setTextColorMenuItem] = useState("black");
    const [showTextPicker, setShowTextPicker] = useState(false);

    // Background color menu and chooser
    const [backgroundColor, setBackgroundColor] = useState("white");
    //const [nextCustomBackground, setNextCustomBackground] = useState(1);
    const [backgroundOpacity, setBackgroundOpacity] = useState(1);
    const [backgroundChooserSwatches, setBackgroundChooserSwatches] = useState(
        defaultBackgroundColors
    );
    // We need a separate copy of the array here because we will modify the menu items array with
    // items that we don't want to appear in the swatches (specifically the gradients).
    let defaultBackgroundMenuItems = defaultBackgroundColors.slice();
    // We insert the Super Bible gradients after our partial transparency menu item,
    // but before New...
    defaultBackgroundMenuItems = defaultBackgroundMenuItems.concat(
        specialColors
    );
    const [backgroundColorMenuItems, setBackgroundColorMenuItems] = useState(
        defaultBackgroundMenuItems.concat(newMenuItem)
    );
    const [backgroundColorMenuItem, setBackgroundColorMenuItem] = useState(
        "white"
    );
    const [showBackgroundPicker, setShowBackgroundPicker] = useState(false);

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
            // Enhance: When the bubble spec has opacity, update it here
            //setBackgroundOpacity(currentBubbleSpec.opacity ? currentBubbleSpec.opacity : 1);
            // N.B. Don't forget to add spec opacity to call to 'getSwatchFromHex' (and maybe rename the method).
            const backColor = getBackgroundColorValue(currentBubbleSpec);
            if (!isSpecialColorName(backColor)) {
                const newSwatch = getSwatchFromHex(
                    backColor,
                    "somename"
                    //                    getNewCustomBackgroundName()
                );
                // Add it to the menu, if it's not already there.
                const newBackgroundMenu = getNewMenuArrayWithSwatch(
                    backgroundColorMenuItems,
                    newSwatch,
                    backgroundMenuCustomInsertionIndex
                );
                if (newBackgroundMenu) {
                    setBackgroundColorMenuItems(newBackgroundMenu);
                    // Since it's not one of our standard backgrounds, also add it to the chooser swatches
                    const newSwatchArray = getNewSwatchArrayForChooser(
                        backgroundChooserSwatches,
                        newSwatch
                    );
                    if (newSwatchArray) {
                        setBackgroundChooserSwatches(newSwatchArray);
                    }
                    setBackgroundColorMenuItem(newSwatch.name);
                }
            }
            // This didn't work for the case where we're just adding a new menu item; but it'll catch
            // all the other cases.
            const menuItem = getBackgroundMenuItemFromColorName(backColor);
            if (menuItem) {
                setBackgroundColorMenuItem(menuItem.name);
            }
            setBackgroundColor(backColor);

            // Get the current bubble's textColor and set it
            const bubbleTextColor = ComicTool.bubbleManager().getTextColor();
            const newSwatch = getSwatchFromHex(
                bubbleTextColor,
                "somename"
                //                getNewCustomTextName()
            );
            // Add it to the menu, if it's not already there.
            const newTextMenu = getNewMenuArrayWithSwatch(
                textColorMenuItems,
                newSwatch,
                textMenuCustomInsertionIndex
            );
            if (newTextMenu) {
                setTextColorMenuItems(newTextMenu);
                // Since it's not one of our standard text colors, also add it to the chooser swatches
                const newSwatchArray = getNewSwatchArrayForChooser(
                    textChooserSwatches,
                    newSwatch
                );
                if (newSwatchArray) {
                    setTextChooserSwatches(newSwatchArray);
                }
                setTextColorMenuItem(newSwatch.name);
            }
            setTextColor(bubbleTextColor);
        } else {
            setBubbleActive(false);
        }
    }, [currentBubbleSpec]);

    // Set/Reset listeners for Esc/Enter to close chooser when open
    useEffect(() => {
        if (showTextPicker) {
            setKeyListener();
        } else {
            // If no bubble is active, we are probably still getting setup, so we'll skip this "change".
            if (bubbleActive) {
                resetKeyListener();
                handleNewTextColorChooserValue();
            }
        }
    }, [showTextPicker]);

    // Set/Reset listeners for Esc/Enter to close chooser when open
    useEffect(() => {
        if (showBackgroundPicker) {
            setKeyListener();
        } else {
            // If no bubble is active, we are probably still getting setup, so we'll skip this "change".
            if (bubbleActive) {
                resetKeyListener();
                handleNewBackgroundColorChooserValue();
            }
        }
    }, [showBackgroundPicker]);

    const getBackgroundMenuItemFromColorName = (
        backColorName: string
    ): IMenuItem | undefined => {
        return backgroundColorMenuItems.find(
            item => item.name === backColorName
        );
    };

    const getTextMenuItemFromColorName = (
        textColorName: string
    ): IMenuItem | undefined => {
        return textColorMenuItems.find(item => item.name === textColorName);
    };

    const isSwatchInMenuArray = (
        array: IMenuItem[],
        swatch: ISwatchDefn
    ): boolean => !!array.find(swatchCompareFunc(swatch));

    // Function for comparing a swatch with an array of IMenuItems to see if the swatch is already
    // in the array. We pass this function to .find().
    const swatchCompareFunc = (swatch: ISwatchDefn) => (
        item: IMenuItem
    ): boolean => {
        if (item.colors.length > 1 || item.name === "new") {
            return false;
        }
        const itemOpacity = item.opacity ? item.opacity : 1;
        const swatchColor = tinycolor(swatch.colors[0]);
        const itemColor = tinycolor(item.colors[0]);
        return (
            swatchColor.toHex() === itemColor.toHex() &&
            swatch.opacity === itemOpacity
        );
    };

    const isSpecialColorName = (colorName: string): boolean =>
        !!specialColors.find(item => item.name === colorName);

    // const getSwatchFromHex = (
    //     hexColor: string,
    //     customName: string,
    //     opacity?: number
    // ): ISwatchDefn => {
    //     return {
    //         name: customName,
    //         colors: [hexColor],
    //         opacity: opacity ? opacity : 1
    //     };
    // };

    // const getNewCustomTextName = (): string => {
    //     const nextNumber = nextCustomText;
    //     setNextCustomText(nextNumber + 1);
    //     return `Custom${nextNumber}`;
    // };

    // const getNewCustomBackgroundName = (): string => {
    //     const nextNumber = nextCustomBackground;
    //     setNextCustomBackground(nextNumber + 1);
    //     return `Custom${nextNumber}`;
    // };

    const setKeyListener = () => {
        document.addEventListener("keydown", handleKeyPress, false);
    };

    const resetKeyListener = () => {
        document.removeEventListener("keydown", handleKeyPress, false);
    };

    // When color choosers are visible, Enter or Esc should close them
    const handleKeyPress = (e: KeyboardEvent) => {
        if (e.defaultPrevented) {
            return;
        }
        if (e.key === "Escape" || e.key === "Enter") {
            if (showBackgroundPicker) {
                handleBackgroundPickerClose(undefined);
            }
            if (showTextPicker) {
                handleTextPickerClose(undefined);
            }
        }
    };

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
        const specialFound = specialColors.find(
            elem => elem.colors![1] === spec.backgroundColors![1]
        );
        if (specialFound) {
            return specialFound.name;
        }
        // maybe from a later version of Bloom? All we can do.
        return "white";
    };

    // Callback when we complete a bubble text color change via the new color chooser.
    // We add a new (unique) color to the list of swatches for the chooser AND to the list of menu options.
    // After our list of swatches grows past 12, we start bumping old custom values.
    // The combo dropdown menus can grow to around 7 options before we start bumping custom values.
    const handleNewTextColorChooserValue = () => {
        ComicTool.bubbleManager().setTextColor(textColor);
        const newSwatchColor: ISwatchDefn = getSwatchFromHex(
            textColor,
            "somename"
            //            getNewCustomTextName()
        );
        const newSwatchArray = getNewSwatchArrayForChooser(
            textChooserSwatches,
            newSwatchColor
        );
        if (newSwatchArray) {
            setTextChooserSwatches(newSwatchArray);
        }

        const newMenuArray = getNewMenuArrayWithSwatch(
            textColorMenuItems,
            newSwatchColor,
            textMenuCustomInsertionIndex
        );
        if (newMenuArray) {
            setTextColorMenuItems(newMenuArray);
            setTextColorMenuItem(newSwatchColor.name);
        } else {
            setTextColorMenuItem(
                getTextColorMenuItemBySwatch(newSwatchColor).name
            );
        }
    };

    // Callback when we complete a bubble background color change via the new color chooser.
    // We add a new (unique) color to the list of swatches for the chooser AND to the list of menu options.
    // After our list of swatches grows past 12, we start bumping old custom values.
    // The combo dropdown menus can grow to around 7 options before we start bumping custom values.
    const handleNewBackgroundColorChooserValue = () => {
        const newSwatchColor: ISwatchDefn = getSwatchFromHex(
            backgroundColor,
            "somename",
            //            getNewCustomBackgroundName(),
            backgroundOpacity
        );
        const newSwatchArray = getNewSwatchArrayForChooser(
            backgroundChooserSwatches,
            newSwatchColor
        );
        if (newSwatchArray) {
            setBackgroundChooserSwatches(newSwatchArray);
        }

        const newMenuArray = getNewMenuArrayWithSwatch(
            backgroundColorMenuItems,
            newSwatchColor,
            backgroundMenuCustomInsertionIndex
        );
        if (newMenuArray) {
            setBackgroundColorMenuItems(newMenuArray);
            setBackgroundColorMenuItem(newSwatchColor.name);
        } else {
            setBackgroundColorMenuItem(
                getBackgroundColorMenuItemBySwatch(newSwatchColor).name
            );
        }
    };

    const getBackgroundColorMenuItemBySwatch = (
        swatch: ISwatchDefn
    ): IMenuItem => {
        return backgroundColorMenuItems.find(
            swatchCompareFunc(swatch)
        ) as IMenuItem;
    };

    const getTextColorMenuItemBySwatch = (swatch: ISwatchDefn): IMenuItem => {
        return textColorMenuItems.find(swatchCompareFunc(swatch)) as IMenuItem;
    };

    const getNewSwatchArrayForChooser = (
        chooserSwatchArray: IMenuItem[],
        swatch: ISwatchDefn
    ): IMenuItem[] | undefined => {
        if (isSwatchInMenuArray(chooserSwatchArray, swatch)) {
            // this one is in the list already
            return undefined;
        }
        const newSwatchArray = chooserSwatchArray.slice();
        newSwatchArray.splice(0, 0, swatch);
        if (newSwatchArray.length > maxChooserSwatches) {
            newSwatchArray.splice(newSwatchArray.length - 4, 1); // Delete the oldest custom value
        }
        return newSwatchArray;
    };

    // Given an array of IMenuItem and an ISwatchDefn (and insertion index), returns a new menu array
    // or undefined, if the Swatch is already in the menu.
    const getNewMenuArrayWithSwatch = (
        menuArray: IMenuItem[],
        swatch: ISwatchDefn,
        insertionIndex: number
    ): IMenuItem[] | undefined => {
        if (isSwatchInMenuArray(menuArray, swatch)) {
            // this one is in the Array already
            return undefined;
        }
        const newMenuArray = menuArray.slice();
        newMenuArray.splice(insertionIndex, 0, swatch); // insert new item
        if (newMenuArray.length > maxMenuItems) {
            newMenuArray.splice(newMenuArray.length - 2, 1); // delete the menu item just before "New..."
        }
        return newMenuArray;
    };

    // Callback when a new bubble background color menu item is chosen.
    // Enhance: event.target must somehow include opacity!
    const handleBackgroundColorChanged = event => {
        const newValue = event.target.value as string;

        if (newValue === "new") {
            // get color from color chooser
            setShowBackgroundPicker(true);
            return;
        }

        const menuItem = getBackgroundMenuItemFromColorName(newValue);
        if (menuItem) {
            updateBackgroundColor(menuItem);
        }
    };

    // We come into this from 2 places: just above from changing the Select combo and from chooser change
    const updateBackgroundColor = (newColorSwatch: ISwatchDefn) => {
        // Update the toolbox controls
        if (getBackgroundMenuItemFromColorName(newColorSwatch.name)) {
            setBackgroundColorMenuItem(newColorSwatch.name);
        }
        setBackgroundColor(newColorSwatch.colors[0]);
        const opacity = newColorSwatch.opacity ? newColorSwatch.opacity : 1;
        setBackgroundOpacity(opacity);

        // Update the Comical canvas on the page frame
        const backgroundColors = newColorSwatch.colors;
        ComicTool.bubbleManager().updateSelectedItemBubbleSpec({
            backgroundColors: backgroundColors
            // opacity: newColorSwatch.opacity?!
        });
    };

    // Callback when the color of the bubble text is changed.
    const handleTextColorChanged = event => {
        const newTextColor = event.target.value as string;

        if (newTextColor === "new") {
            // get color from color chooser
            setShowTextPicker(true);
            return;
        }

        const menuItem = getTextMenuItemFromColorName(newTextColor);
        if (menuItem) {
            updateTextColor(menuItem);
        }
    };

    // We come into this from 2 places: just above from changing the Select combo and from chooser change
    const updateTextColor = (newColorSwatch: IMenuItem) => {
        const color = newColorSwatch.colors[0]; // text color is always monochrome
        // Update the toolbox controls
        if (getTextMenuItemFromColorName(newColorSwatch.name)) {
            setTextColorMenuItem(newColorSwatch.name);
        }
        setTextColor(color);

        ComicTool.bubbleManager().setTextColor(color);
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

    const getBackgroundColorMenu = () => (
        <Select
            value={backgroundColorMenuItem}
            className="bubbleOptionDropdown"
            inputProps={{
                name: "backgroundColorMenuItem",
                id: "bubble-backgroundColor-dropdown"
            }}
            MenuProps={{
                className: "bubble-options-dropdown-menu"
            }}
            onChange={event => {
                handleBackgroundColorChanged(event);
            }}
        >
            {backgroundColorMenuItems.map((item: IMenuItem, i: number) => (
                <MenuItem value={item.name} key={i}>
                    <MenuColorBar {...item} key={i} />
                    {/* Not sure why we need key at this level, but it makes the selection show correctly. */}
                </MenuItem>
            ))}
        </Select>
    );

    const reportColorResult = (color: ColorResult) => {
        // Uncomment the code below to get a complete report on the color returned from the chooser.
        // console.log("Color Result:");
        // if (color.hex) {
        //     console.log(`  Hex: ${color.hex ? color.hex : "undefined"}`);
        // }
        // if (color.rgb) {
        //     console.log(
        //         `  RGB: R${color.rgb.r} G${color.rgb.g} B${color.rgb.b} A${
        //             color.rgb.a
        //         }`
        //     );
        // }
        // if (color.hsl) {
        //     console.log(
        //         `  HSL: H${color.hsl.h} S${color.hsl.s} L${color.hsl.l} A${
        //             color.hsl.a
        //         }`
        //     );
        // }
    };

    const handleBackgroundPickerChange = (color: ISwatchDefn) => {
        //reportColorResult(color);
        updateBackgroundColor(color);
        //     getSwatchFromHex(
        //         color.hex,
        //         getNewCustomBackgroundName(),
        //         color.rgb.a
        //     )
        // );
    };

    const handleBackgroundPickerClose = (
        event: React.MouseEvent | undefined
    ) => {
        setShowBackgroundPicker(false);
    };

    const getTextColorMenu = () => (
        <Select
            value={textColorMenuItem}
            className="bubbleOptionDropdown"
            inputProps={{
                name: "textColorMenuItem",
                id: "bubble-textColor-dropdown"
            }}
            MenuProps={{
                className: "bubble-options-dropdown-menu"
            }}
            onChange={event => {
                handleTextColorChanged(event);
            }}
        >
            {textColorMenuItems.map((item: IMenuItem, i: number) => (
                <MenuItem value={item.name} key={i}>
                    <MenuColorBar {...item} key={i} />
                    {/* Not sure why we need key at this level, but it makes the selection show correctly. */}
                </MenuItem>
            ))}
        </Select>
    );

    const handleTextPickerChange = (color: ISwatchDefn) => {
        //reportColorResult(color);
        updateTextColor(color);
    };

    const handleTextPickerClose = (event: React.MouseEvent | undefined) => {
        setShowTextPicker(false);
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
                        <InputLabel htmlFor="bubble-textColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.TextColor">
                                Text Color
                            </Span>
                        </InputLabel>
                        {getTextColorMenu()}
                        {showTextPicker && (
                            <div className="pickerWrapper">
                                <div
                                    className="pickerCover"
                                    onClick={handleTextPickerClose}
                                />
                                <CustomColorPicker
                                    noAlphaSlider={true}
                                    currentColor={{
                                        name: "someName",
                                        colors: [textColor],
                                        opacity: 1
                                    }}
                                    swatchColors={textChooserSwatches}
                                    onChange={(color: ISwatchDefn) =>
                                        handleTextPickerChange(color)
                                    }
                                />
                            </div>
                        )}
                    </FormControl>
                    <FormControl>
                        <InputLabel htmlFor="bubble-backgroundColor-dropdown">
                            <Span l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor">
                                Background Color
                            </Span>
                        </InputLabel>
                        {getBackgroundColorMenu()}
                        {showBackgroundPicker && (
                            <div className="pickerWrapper">
                                <div
                                    className="pickerCover"
                                    onClick={handleBackgroundPickerClose}
                                />
                                <CustomColorPicker
                                    currentColor={{
                                        name: "someName",
                                        colors: [backgroundColor],
                                        opacity: 1
                                    }}
                                    swatchColors={backgroundChooserSwatches}
                                    onChange={(color: ISwatchDefn) =>
                                        handleBackgroundPickerChange(color)
                                    }
                                />
                            </div>
                        )}
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
