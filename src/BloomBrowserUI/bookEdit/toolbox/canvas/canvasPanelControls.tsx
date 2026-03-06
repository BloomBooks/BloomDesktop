// Toolbox panel control renderers for canvas elements.
//
// Responsibilities of this module:
// - Implement the concrete React controls used in the Canvas toolbox side panel
//   (style/tail/rounded-corners/outline/text/background/image-fit).
// - Read current state from `ICanvasToolsPanelState` and dispatch user changes via
//   panel callbacks.
// - Apply local UI constraints that depend on current element state (for example,
//   disabling tail on child bubbles or image-fit options for non-image contexts).
//
// How this fits with the declarative system:
// - `canvasControlRegistry.ts` references these components for panel-type controls.
// - `canvasElementDefinitions.ts` chooses which panel sections are shown per element.
// - `canvasControlHelpers.ts` resolves section/control composition at runtime.

import { ThemeProvider } from "@emotion/react";
import * as React from "react";
import FormControl from "@mui/material/FormControl";
import { MenuItem } from "@mui/material";
import InputLabel from "@mui/material/InputLabel";
import { BubbleSpec } from "comicaljs";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import { Div, Span } from "../../../react_components/l10nComponents";
import { ColorBar } from "./colorBar";
import BloomSelect from "../../../react_components/bloomSelect";
import { toolboxMenuPopupTheme } from "../../../bloomMaterialUITheme";
import {
    kBloomButtonClass,
    kImageFitModeContainValue,
    kImageFitModeCoverValue,
} from "./canvasElementConstants";
import {
    ICanvasToolsPanelState,
    IControlContext,
    kImageFitModePaddedValue,
} from "./canvasControlTypes";

const isChild = (bubbleSpec: BubbleSpec | undefined): boolean => {
    const order = bubbleSpec?.order ?? 0;
    return order > 1;
};

const isBubble = (bubbleSpec: BubbleSpec | undefined): boolean => {
    return (
        !!bubbleSpec &&
        bubbleSpec.style !== "none" &&
        bubbleSpec.style !== "caption"
    );
};

const styleSupportsRoundedCorners = (
    currentBubbleSpec: BubbleSpec | undefined,
): boolean => {
    if (!currentBubbleSpec) {
        return false;
    }

    const bgColors = currentBubbleSpec.backgroundColors;
    if (bgColors && bgColors.includes("transparent")) {
        return false;
    }

    switch (currentBubbleSpec.style) {
        case "caption":
            return true;
        case "none":
            return !!bgColors && bgColors.length > 0;
        default:
            return false;
    }
};

const getCurrentBubbleSpec = (
    panelState: ICanvasToolsPanelState,
): BubbleSpec | undefined => {
    return panelState.currentBubble?.getBubbleSpec() as BubbleSpec | undefined;
};

export const BubbleStylePanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    return (
        <FormControl variant="standard">
            <InputLabel htmlFor="canvasElement-style-dropdown">
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.Style">
                    Style
                </Span>
            </InputLabel>
            <ThemeProvider theme={toolboxMenuPopupTheme}>
                <BloomSelect
                    variant="standard"
                    value={props.panelState.style}
                    onChange={(event) => {
                        props.panelState.onStyleChanged(event);
                    }}
                    className="canvasElementOptionDropdown"
                    inputProps={{
                        name: "style",
                        id: "canvasElement-style-dropdown",
                    }}
                    MenuProps={{
                        className: "canvasElement-options-dropdown-menu",
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
                </BloomSelect>
            </ThemeProvider>
        </FormControl>
    );
};

export const ShowTailPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    return (
        <BloomCheckbox
            label="Show Tail"
            l10nKey="EditTab.Toolbox.ComicTool.Options.ShowTail"
            checked={props.panelState.showTail}
            disabled={
                isChild(props.panelState.selectedItemSpec) ||
                props.ctx.canvasElement.classList.contains(kBloomButtonClass)
            }
            onCheckChanged={(value) => {
                props.panelState.onShowTailChanged(value as boolean);
            }}
        />
    );
};

export const RoundedCornersPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    return (
        <BloomCheckbox
            label="Rounded Corners"
            l10nKey="EditTab.Toolbox.ComicTool.Options.RoundedCorners"
            checked={props.panelState.roundedCorners}
            disabled={
                !styleSupportsRoundedCorners(
                    getCurrentBubbleSpec(props.panelState),
                )
            }
            onCheckChanged={(value) => {
                props.panelState.onRoundedCornersChanged(value);
            }}
        />
    );
};

export const OutlineColorPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    const bubbleSpec = getCurrentBubbleSpec(props.panelState);
    const canEditOutline = isBubble(bubbleSpec);

    return (
        <FormControl
            variant="standard"
            className={canEditOutline ? "" : "disabled"}
        >
            <InputLabel htmlFor="canvasElement-outlineColor-dropdown">
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.OuterOutlineColor">
                    Outer Outline Color
                </Span>
            </InputLabel>
            <ThemeProvider theme={toolboxMenuPopupTheme}>
                <BloomSelect
                    variant="standard"
                    value={
                        props.panelState.outlineColor
                            ? props.panelState.outlineColor
                            : "none"
                    }
                    className="canvasElementOptionDropdown"
                    inputProps={{
                        name: "outlineColor",
                        id: "canvasElement-outlineColor-dropdown",
                    }}
                    MenuProps={{
                        className: "canvasElement-options-dropdown-menu",
                    }}
                    onChange={(event) => {
                        if (canEditOutline) {
                            props.panelState.onOutlineColorChanged(event);
                        }
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
                        <Div l10nKey="Common.Colors.Crimson">Crimson</Div>
                    </MenuItem>
                </BloomSelect>
            </ThemeProvider>
        </FormControl>
    );
};

export const TextColorPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    return (
        <FormControl variant="standard">
            <InputLabel htmlFor="text-color-bar" shrink={true}>
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.TextColor">
                    Text Color
                </Span>
            </InputLabel>
            <ColorBar
                id="text-color-bar"
                onClick={props.panelState.openTextColorChooser}
                colorInfo={props.panelState.textColorSwatch}
                isDefault={props.panelState.textColorIsDefault}
            />
        </FormControl>
    );
};

export const BackgroundColorPanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    const isCaption =
        getCurrentBubbleSpec(props.panelState)?.style === "caption";

    return (
        <FormControl variant="standard">
            <InputLabel shrink={true} htmlFor="background-color-bar">
                <Span l10nKey="EditTab.Toolbox.ComicTool.Options.BackgroundColor">
                    Background Color
                </Span>
            </InputLabel>
            <ColorBar
                id="background-color-bar"
                onClick={() =>
                    props.panelState.openBackgroundColorChooser(!isCaption)
                }
                colorInfo={props.panelState.backgroundColorSwatch}
                text={props.panelState.percentTransparencyString}
            />
        </FormControl>
    );
};

export const ImageFillModePanelControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
}> = (props) => {
    return (
        <FormControl variant="standard">
            <InputLabel htmlFor="image-fill-mode-dropdown">
                <Span l10nKey="EditTab.Toolbox.CanvasTool.ImageFit">
                    Image Fit
                </Span>
            </InputLabel>
            <ThemeProvider theme={toolboxMenuPopupTheme}>
                <BloomSelect
                    variant="standard"
                    value={props.panelState.imageFillMode}
                    onChange={(event) => {
                        props.panelState.onImageFillChanged(event);
                    }}
                    className="canvasElementOptionDropdown"
                    inputProps={{
                        name: "imageFillMode",
                        id: "image-fill-mode-dropdown",
                    }}
                    MenuProps={{
                        className: "canvasElement-options-dropdown-menu",
                    }}
                >
                    <MenuItem value={kImageFitModePaddedValue}>
                        <Div l10nKey="EditTab.Toolbox.CanvasTool.ImageFit.Margin">
                            Fit with Margin
                        </Div>
                    </MenuItem>
                    <MenuItem value={kImageFitModeContainValue}>
                        <Div l10nKey="EditTab.Toolbox.CanvasTool.ImageFit.FitToEdge">
                            Fit to Edge
                        </Div>
                    </MenuItem>
                    <MenuItem value={kImageFitModeCoverValue}>
                        <Div l10nKey="EditTab.Toolbox.CanvasTool.ImageFit.Fill">
                            Fill
                        </Div>
                    </MenuItem>
                </BloomSelect>
            </ThemeProvider>
        </FormControl>
    );
};
