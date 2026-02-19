import * as React from "react";
import { SvgIconProps } from "@mui/material";
import { Bubble } from "comicaljs";
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
    | "image"
    | "imagePanel"
    | "video"
    | "audio"
    | "linkGrid"
    | "url"
    | "bubble"
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
    isLinkGrid: boolean;
    isNavigationButton: boolean;
    isButton: boolean;
    isBookGrid: boolean;
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

export interface IControlSurfaceRule {
    visible?: (ctx: IControlContext) => boolean;
    enabled?: (ctx: IControlContext) => boolean;
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
    icon?: React.ReactNode;
    disabled?: boolean;
    featureName?: string;
    subscriptionTooltipOverride?: string;
    shortcut?: IControlShortcut;
    availability?: {
        visible?: (ctx: IControlContext) => boolean;
        enabled?: (ctx: IControlContext) => boolean;
    };
    separatorAbove?: boolean;
    subMenuItems?: IControlMenuRow[];
    onSelect: (ctx: IControlContext, runtime: IControlRuntime) => Promise<void>;
}

export interface IControlMenuHelpRow {
    kind: "help";
    helpRowL10nId: string;
    helpRowEnglish: string;
    separatorAbove?: boolean;
    availability?: {
        visible?: (ctx: IControlContext) => boolean;
    };
}

export type IControlMenuRow = IControlMenuCommandRow | IControlMenuHelpRow;

export interface IBaseControlDefinition {
    id: TopLevelControlId;
    featureName?: string;
    l10nId: string;
    englishLabel: string;
    icon?: IControlIcon;
    tooltipL10nId?: string;
}

export interface ICommandControlDefinition extends IBaseControlDefinition {
    kind: "command";
    action: (ctx: IControlContext, runtime: IControlRuntime) => Promise<void>;
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
    showTail: boolean;
    setShowTail: (v: boolean) => void;
    roundedCorners: boolean;
    setRoundedCorners: (v: boolean) => void;
    outlineColor: string | undefined;
    setOutlineColor: (c: string | undefined) => void;
    textColorSwatch: IColorInfo;
    setTextColorSwatch: (c: IColorInfo) => void;
    backgroundColorSwatch: IColorInfo;
    setBackgroundColorSwatch: (c: IColorInfo) => void;
    imageFillMode: ImageFillMode;
    setImageFillMode: (m: ImageFillMode) => void;
    currentBubble: Bubble | undefined;
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
