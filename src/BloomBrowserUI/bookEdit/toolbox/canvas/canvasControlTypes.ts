// Shared type contracts for the canvas control system.
//
// How this file fits with the other canvas-control modules:
// - `canvasControlRegistry.ts` defines the concrete controls (action/menu/panel metadata)
//   and section membership, using these interfaces.
// - `canvasControlAvailabilityPresets.ts` defines reusable visibility/enabled policy fragments
//   typed against `AvailabilityRulesMap` and `IControlContext`.
// - `canvasElementDefinitions.ts` is the declarative map from canvas element type to
//   toolbar/menu/tool-panel layout and availability rules.
// - `canvasPanelControls.tsx` implements panel control components that satisfy
//   `ICanvasToolsPanelState` + `IControlContext` contracts defined here.
//
// Keep this file dependency-light and declarative: it is the schema that lets the
// rest of the modules compose consistently.
import * as React from "react";
import { SvgIconProps } from "@mui/material";
import { Bubble, BubbleSpec } from "comicaljs";
import { IColorInfo } from "../../../react_components/color-picking/colorSwatch";
import {
    kImageFitModeContainValue,
    kImageFitModeCoverValue,
} from "./canvasElementConstants";
import { CanvasElementType } from "./canvasElementTypes";

export const kImageFitModePaddedValue = "padded";

export type ImageFillMode =
    | typeof kImageFitModePaddedValue
    | typeof kImageFitModeContainValue
    | typeof kImageFitModeCoverValue;

export type ControlId =
    | "chooseImage"
    | "pasteImage"
    | "copyImage"
    | "missingMetadata"
    | "resetImage"
    | "expandToFillSpace"
    | "imageFillMode"
    | "chooseVideo"
    | "recordVideo"
    | "playVideoEarlier"
    | "playVideoLater"
    | "format"
    | "copyText"
    | "pasteText"
    | "autoHeight"
    | "fillBackground"
    | "addChildBubble"
    | "bubbleStyle"
    | "showTail"
    | "roundedCorners"
    | "textColor"
    | "backgroundColor"
    | "outlineColor"
    | "setDestination"
    | "linkGridChooseBooks"
    | "duplicate"
    | "delete"
    | "toggleDraggable"
    | "togglePartOfRightAnswer"
    | "chooseAudio"
    | "removeAudio"
    | "playCurrentAudio"
    | "useTalkingBookTool";

export type TopLevelControlId = Exclude<
    ControlId,
    "removeAudio" | "playCurrentAudio" | "useTalkingBookTool"
>;

export type SectionId =
    | "gameDraggable"
    | "image"
    | "imagePanel"
    | "video"
    | "audio"
    | "linkGrid"
    | "url"
    | "bubble"
    | "outline"
    | "text"
    | "wholeElement";

export interface IControlContext {
    canvasElement: HTMLElement;
    page: HTMLElement | null;
    elementType: CanvasElementType;
    hasImage: boolean;
    hasRealImage: boolean;
    hasVideo: boolean;
    hasPreviousVideoContainer: boolean;
    hasNextVideoContainer: boolean;
    hasText: boolean;
    isRectangle: boolean;
    rectangleHasBackground: boolean;
    isCropped: boolean;
    isNavigationButton: boolean;
    isButton: boolean;
    isBackgroundImage: boolean;
    isSpecialGameElement: boolean;
    canModifyImage: boolean;
    canExpandBackgroundImage: boolean;
    missingMetadata: boolean;
    isInDraggableGame: boolean;
    canChooseAudioForElement: boolean;
    hasCurrentImageSound: boolean;
    currentImageSoundLabel: string | undefined;
    canToggleDraggability: boolean;
    hasDraggableId: boolean;
    hasDraggableTarget: boolean;
    textHasAudio: boolean | undefined;
}

export interface IControlRuntime {
    closeMenu: (launchingDialog?: boolean) => void;
}

export type IControlIcon =
    | React.FunctionComponent<SvgIconProps>
    | React.ReactNode;

export interface IControlShortcut {
    id: string;
    display: string;
    matches?: (e: KeyboardEvent) => boolean;
}

export type IControlAvailability =
    | boolean
    | ((ctx: IControlContext) => boolean);

export interface IControlSurfaceRule {
    visible?: IControlAvailability;
    enabled?: IControlAvailability;
}

export interface IControlRule extends IControlSurfaceRule {
    surfacePolicy?: Partial<
        Record<"toolbar" | "menu" | "toolPanel", IControlSurfaceRule>
    >;
}

export interface IControlMenuCommandRow {
    kind?: "command";
    id?: ControlId;
    l10nId?: string;
    englishLabel?: string;
    subLabelL10nId?: string;
    subLabel?: string;
    // Optional one-line help content rendered after this command row.
    // We model this as metadata on command rows (rather than a separate
    // row kind) so section filtering/availability stays simpler.
    helpRowL10nId?: string;
    helpRowEnglish?: string;
    helpRowSeparatorAbove?: boolean;
    icon?: React.ReactNode;
    disabled?: boolean;
    featureName?: string;
    subscriptionTooltipOverride?: string;
    shortcut?: IControlShortcut;
    availability?: IControlSurfaceRule;
    separatorAbove?: boolean;
    subMenuItems?: IControlMenuRow[];
    // Menu handlers may be sync or async.
    // Sync example: toggling a class or opening an in-process dialog launcher.
    // Async example: awaiting `showDialogToChooseSoundFileAsync` before
    // setting `data-sound`.
    onSelect: (
        ctx: IControlContext,
        runtime: IControlRuntime,
    ) => void | Promise<void>;
}

export type IControlMenuRow = IControlMenuCommandRow;

export interface IBaseControlDefinition {
    id: TopLevelControlId;
    featureName?: string;
    l10nId: string;
    englishLabel: string;
    // Optional help line to show beneath the default menu row for this control.
    helpRowL10nId?: string;
    helpRowEnglish?: string;
    helpRowSeparatorAbove?: boolean;
    icon?: IControlIcon;
    tooltipL10nId?: string;
}

export interface ICommandControlDefinition extends IBaseControlDefinition {
    kind: "command";
    // Action handlers follow the same sync-or-async contract as menu rows.
    // Prefer sync unless an awaited dependency is truly required.
    action: (
        ctx: IControlContext,
        runtime: IControlRuntime,
    ) => void | Promise<void>;
    toolbar?: {
        relativeSize?: number;
        icon?: IControlIcon;
        render?: (
            ctx: IControlContext,
            runtime: IControlRuntime,
        ) => React.ReactNode;
    };
    menu?: {
        icon?: React.ReactNode;
        subLabelL10nId?: string;
        shortcutDisplay?: string;
        buildMenuItem?: (
            ctx: IControlContext,
            runtime: IControlRuntime,
        ) => IControlMenuCommandRow;
    };
}

export interface IPanelOnlyControlDefinition extends IBaseControlDefinition {
    kind: "panel";
    canvasToolsControl: React.FunctionComponent<{
        ctx: IControlContext;
        panelState: ICanvasToolsPanelState;
    }>;
}

export type IControlDefinition =
    | ICommandControlDefinition
    | IPanelOnlyControlDefinition;

export interface IControlSection {
    id: SectionId;
    controlsBySurface: Partial<
        Record<"menu" | "toolPanel", TopLevelControlId[]>
    >;
}

export interface ICanvasToolsPanelState {
    style: string;
    setStyle: (s: string) => void;
    onStyleChanged: (event: unknown) => void;
    showTail: boolean;
    setShowTail: (v: boolean) => void;
    onShowTailChanged: (value: boolean) => void;
    roundedCorners: boolean;
    setRoundedCorners: (v: boolean) => void;
    onRoundedCornersChanged: (value: boolean | undefined) => void;
    outlineColor: string | undefined;
    setOutlineColor: (c: string | undefined) => void;
    onOutlineColorChanged: (event: unknown) => void;
    textColorSwatch: IColorInfo;
    setTextColorSwatch: (c: IColorInfo) => void;
    textColorIsDefault: boolean;
    openTextColorChooser: () => void;
    backgroundColorSwatch: IColorInfo;
    setBackgroundColorSwatch: (c: IColorInfo) => void;
    percentTransparencyString: string | undefined;
    openBackgroundColorChooser: (transparency: boolean) => void;
    imageFillMode: ImageFillMode;
    setImageFillMode: (m: ImageFillMode) => void;
    onImageFillChanged: (event: unknown) => void;
    currentBubble: Bubble | undefined;
    selectedItemSpec: BubbleSpec | undefined;
}

export interface ICanvasElementDefinition {
    type: CanvasElementType;
    menuSections: SectionId[];
    toolbar: Array<TopLevelControlId | "spacer">;
    toolPanel: SectionId[];
    availabilityRules: Partial<
        Record<TopLevelControlId, IControlRule | "exclude">
    >;
}

export type AvailabilityRulesMap =
    ICanvasElementDefinition["availabilityRules"];

export interface IResolvedControl {
    control: IControlDefinition;
    enabled: boolean;
    menuRow?: IControlMenuCommandRow;
}
