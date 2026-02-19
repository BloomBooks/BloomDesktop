# Canvas Controls Design

## Goal

Define a single, registry-driven system where:

1. **Every possible control** (toolbar button, menu item, CanvasTool side-panel widget) lives in one place as a control definition.
2. **Each canvas element type** declares which controls it uses—declaratively, without writing per-control code—and which surface each control/section appears on.
3. **New controls** are added once to the control registry; element types opt in by listing the control in their toolbar, menu, or tool panel.
4. **Shared definition**: icon, label, l10n key, and command action or panel renderer are defined once in the control definition and used by all surfaces. Visible/enabled logic lives in the element definition via composable shared presets, so each element file is fully self-describing.
5. **Subscription capability metadata**: controls may define `featureName` when they map to a subscription-gated feature. Controls with no mapped feature omit it.

---

## Core Concepts

### ControlId

A string literal union of every control-related id used by this system: top-level commands plus dynamic menu row ids. Adding a new top-level control means adding to this union and to the control registry; adding a dynamic row id only requires adding to this union.

```ts
export type ControlId =
  // image commands
  | "chooseImage"
  | "pasteImage"
  | "copyImage"
  | "missingMetadata"
  | "resetImage"
  | "expandToFillSpace"
  | "imageFillMode"
  // video commands
  | "chooseVideo"
  | "recordVideo"
  | "playVideoEarlier"
  | "playVideoLater"
  // text / bubble commands
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
  // navigation
  | "setDestination"
  // link grid
  | "linkGridChooseBooks"
  // whole-element
  | "duplicate"
  | "delete"
  | "toggleDraggable"
  | "togglePartOfRightAnswer"
  // audio (top-level menu/toolbar command)
  | "chooseAudio"
  // audio submenu row ids (built by chooseAudio.menu.buildMenuItem)
  | "removeAudio"
  | "playCurrentAudio"
  | "useTalkingBookTool";
```

---

### Surfaces

| Surface    | Declared by element as | Rendered by                      |
|------------|------------------------|----------------------------------|
| Toolbar    | `toolbar`              | `CanvasElementContextControls`   |
| Menu       | `menuSections`         | MUI `<Menu>` (right-click / `…`) |
| Tool panel | `toolPanel`            | `CanvasToolControls.tsx`         |

The element definition owns which commands/sections appear on which surface (placement and ordering).
Control definitions may include optional surface-specific rendering metadata,
but they do not decide where they appear.

---

### Command Prototype

`IControlDefinition` is a discriminated union with two kinds:

- `kind: "command"` for controls that execute actions.
- `kind: "panel"` for controls that render panel-only UI.

Visibility/enabled policy is not in control definitions; it belongs to element
`availabilityRules`.

`IControlMenuRow` is a runtime menu-row descriptor used by dynamic menu builders
(`menu.buildMenuItem`). It supports command rows and help rows.

```ts
export interface IControlContext {
  canvasElement: HTMLElement;
  page: HTMLElement | null;
  elementType: CanvasElementType;
  // derived facts computed once before rendering
  hasImage: boolean;
  hasRealImage: boolean;
  hasVideo: boolean;
  hasText: boolean;
  isRectangle: boolean;
  rectangleHasBackground: boolean;
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
  canToggleDraggability: boolean;
  hasDraggableId: boolean;
  hasDraggableTarget: boolean;
  textHasAudio: boolean | undefined;
}

export interface IControlRuntime {
  // Menu uses this to preserve current focus behavior and skipNextFocusChange semantics.
  // Toolbar may pass a no-op implementation.
  closeMenu: (launchingDialog?: boolean) => void;
}

export type IControlIcon =
  | React.FunctionComponent<SvgIconProps>
  | React.ReactNode;

export type IControlMenuRow =
  | IControlMenuCommandRow
  | IControlMenuHelpRow;

export interface IControlMenuCommandRow {
  kind?: "command";
  id?: ControlId;
  l10nId?: string;
  englishLabel?: string;
  subLabelL10nId?: string;
  subLabel?: string;
  icon?: React.ReactNode;
  disabled?: boolean;
  // Optional override. If missing, renderer uses the parent control's featureName.
  featureName?: string;
  subscriptionTooltipOverride?: string;
  // Optional shortcut hint and command trigger.
  // This is intended for command-like menu rows (for example copy/paste),
  // not for visual-only controls.
  shortcut?: {
    id: string;
    // What is rendered at the right side of the menu item, e.g. "Ctrl+C".
    display: string;
    // Optional key matcher used by a keyboard command dispatcher.
    // If omitted, the shortcut is display-only.
    matches?: (e: KeyboardEvent) => boolean;
  };
  // Optional row-level availability for dynamic menu rows (especially submenu rows).
  // This is distinct from element `availabilityRules`, which remains the source of
  // truth for command-level visibility/enabled rules.
  availability?: {
    visible?: (ctx: IControlContext) => boolean;
    enabled?: (ctx: IControlContext) => boolean;
  };
  // Optional visual separator before this row. Useful inside submenus.
  // Section-level menu dividers are automatic and are never modeled as rows.
  separatorAbove?: boolean;
  // Optional one-level submenu. We intentionally do not implement recursive
  // rendering beyond this level.
  subMenuItems?: IControlMenuRow[];
  onSelect: (
    ctx: IControlContext,
    runtime: IControlRuntime,
  ) => Promise<void>;
}

export interface IControlMenuHelpRow {
  kind: "help";
  helpRowL10nId: string;
  helpRowEnglish: string;
  // Optional visual separator before this help row.
  separatorAbove?: boolean;
  availability?: {
    visible?: (ctx: IControlContext) => boolean;
  };
}

export interface IBaseControlDefinition {
  id: ControlId;
  // Optional. When present, this is the key used by the subscription
  // feature-status endpoint to determine enabled state and tier messaging.
  // Examples:
  // - copy/paste style commands: usually omitted
  // - setup hyperlink style commands: typically present
  featureName?: string;

  // --- Shared presentation (used on all surfaces) ---
  l10nId: string;
  englishLabel: string;
  // Either a component reference (for surface-controlled sizing/styling)
  // or a prebuilt node (for exceptional cases like asset-backed icons).
  icon?: IControlIcon;
  tooltipL10nId?: string;
}

export interface ICommandControlDefinition extends IBaseControlDefinition {
  kind: "command";

  // --- Action ---
  // visible/enabled are NOT on the control definition. They live in availabilityRules
  // on the element definition, composed from shared presets.
  // Always async so callers can uniformly await execution, regardless of whether
  // a specific command is currently synchronous.
  action: (ctx: IControlContext, runtime: IControlRuntime) => Promise<void>;

  // --- Surface-specific presentation hints (optional) ---
  // These only affect how the command is rendered, not whether it appears.
  // Placement is controlled entirely by the element definition.
  toolbar?: {
    relativeSize?: number;
    // Optional full renderer override for composite toolbar content
    // (for example icon + text actions such as link-grid choose-books).
    render?: (ctx: IControlContext, runtime: IControlRuntime) => React.ReactNode;
  };
  menu?: {
    subLabelL10nId?: string;
    // Optional shortcut shown on the menu row when this control renders as
    // a single menu item (non-submenu path).
    shortcutDisplay?: string;
    // Optional full menu item builder for controls that need submenu rows
    // or dynamic labels (for example, audio).
    buildMenuItem?: (
      ctx: IControlContext,
      runtime: IControlRuntime,
    ) => IControlMenuCommandRow;
  };
}

export interface IPanelOnlyControlDefinition extends IBaseControlDefinition {
  kind: "panel";

  // --- Tool panel control ---
  // When present, a React component that renders the control in the canvas-tools
  // side panel. Using a component reference (rather than a render function) means
  // the control can own its own hooks without violating React's rules.
  canvasToolsControl: React.FunctionComponent<{
    ctx: IControlContext;
    panelState: ICanvasToolsPanelState;
  }>;
}

export type IControlDefinition =
  | ICommandControlDefinition
  | IPanelOnlyControlDefinition;
```

> **`icon` note:** `icon` is optional — some controls (e.g. text-only menu items) have no icon.
> When `icon` is a `React.FunctionComponent<SvgIconProps>`, each surface instantiates it with its own size props.
> When `icon` is a prebuilt `React.ReactNode`, renderers use it as-is (intended for exceptional asset-backed icons).

### Subscription requirements (`featureName`)

`featureName` is optional on control definitions.

This matches current Bloom UI subscription mechanics:

- If a control/menu row has a `featureName`, feature status is fetched via the `features/status` API.
- Menu rows pass `featureName` to `LocalizableMenuItem`, which already handles:
  - disabled appearance when feature is not enabled,
  - subscription badge display when a non-basic tier is relevant,
  - click-through to subscription settings when unavailable.

Resolution rule for menu rows:

1. If a row has `IControlMenuCommandRow.featureName`, use it.
2. Otherwise use the parent control definition's `featureName`.
3. If neither is present, no subscription behavior is applied for that row.

Help rows (`kind: "help"`) do not carry subscription behavior and are rendered non-clickable.

This lets one control produce submenu rows with different entitlement requirements when needed.

Examples:

```ts
const copyText: IControlDefinition = {
  kind: "command",
  id: "copyText",
  l10nId: "EditTab.Toolbox.ComicTool.Options.CopyText",
  englishLabel: "Copy Text",
  action: async (_ctx, _runtime) => {
    copySelection();
  },
};

const setupHyperlink: IControlDefinition = {
  kind: "command",
  id: "setDestination",
  featureName: "setupHyperlink", // example feature key
  l10nId: "EditTab.SetupHyperlink",
  englishLabel: "Set Up Hyperlink",
  action: async (ctx, runtime) => {
    runtime.closeMenu(true);
    showLinkTargetChooserDialog(/*...*/);
  },
};
```

---

### ICanvasToolsPanelState

Passed to `canvasToolsControl` renderers so they can read and write panel-managed state.

```ts
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
```

---

### Section

Sections are grouping units used by menu and tool panel. They can define different control lists per surface.
Menu dividers are inserted automatically between non-empty sections; they are never declared in section data.

```ts
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

export interface IControlSection {
  id: SectionId;
  controlsBySurface: Partial<Record<"menu" | "toolPanel", ControlId[]>>;
}
```

### The Prototype Registry

```ts
export const controlSections: Record<SectionId, IControlSection> = {
  image: {
    id: "image",
    controlsBySurface: {
      menu: ["missingMetadata", "chooseImage", "pasteImage", "copyImage", "resetImage", "expandToFillSpace"],
    },
  },
  imagePanel: {
    id: "imagePanel",
    controlsBySurface: {
      toolPanel: ["imageFillMode"],
    },
  },
  video: {
    id: "video",
    controlsBySurface: {
      menu: ["chooseVideo", "recordVideo", "playVideoEarlier", "playVideoLater"],
    },
  },
  audio: {
    id: "audio",
    controlsBySurface: {
      menu: ["chooseAudio"],
    },
  },
  linkGrid: {
    id: "linkGrid",
    controlsBySurface: {
      menu: ["linkGridChooseBooks"],
    },
  },
  url: {
    id: "url",
    controlsBySurface: {
      menu: ["setDestination"],
    },
  },
  bubble: {
    id: "bubble",
    controlsBySurface: {
      menu: ["addChildBubble"],
      toolPanel: ["bubbleStyle", "showTail", "roundedCorners", "outlineColor"],
    },
  },
  text: {
    id: "text",
    controlsBySurface: {
      menu: ["format", "copyText", "pasteText", "autoHeight", "fillBackground"],
      toolPanel: ["textColor", "backgroundColor"],
    },
  },
  wholeElement: {
    id: "wholeElement",
    controlsBySurface: {
      menu: ["duplicate", "delete", "toggleDraggable", "togglePartOfRightAnswer"],
    },
  },
};

export const controlRegistry: Record<ControlId, IControlDefinition> = {
  chooseImage: {
    kind: "command",
    id: "chooseImage",
    featureName: "canvas",
    l10nId: "EditTab.Toolbox.ComicTool.Options.ChooseImage",
    englishLabel: "Choose Image from your Computer...",
    icon: SearchIcon,
    action: async (ctx, runtime) => {
      runtime.closeMenu(true);
      const container = ctx.canvasElement.getElementsByClassName(
        kImageContainerClass,
      )[0] as HTMLElement;
      doImageCommand(container, "change");
    },
  },
  missingMetadata: {
    kind: "command",
    id: "missingMetadata",
    featureName: "canvas",
    l10nId: "EditTab.Image.MissingInfo",
    englishLabel: "Missing image information",
    icon: MissingMetadataIcon,
    action: async (ctx, runtime) => {
      runtime.closeMenu(true);
      showCopyrightAndLicenseDialog(/*...*/);
    },
  },
  expandToFillSpace: {
    kind: "command",
    id: "expandToFillSpace",
    featureName: "canvas",
    l10nId: "EditTab.Toolbox.ComicTool.Options.ExpandToFillSpace",
    englishLabel: "Expand to Fill Space",
    icon: FillSpaceIcon,
    action: async (ctx, _runtime) => {
      getCanvasElementManager()?.expandImageToFillSpace();
    },
  },
  chooseAudio: {
    kind: "command",
    id: "chooseAudio",
    featureName: "canvas",
    l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
    englishLabel: "Choose...",
    icon: VolumeUpIcon,
    action: async (_ctx, _runtime) => {},
    menu: {
      buildMenuItem: (ctx, runtime) => {
        if (ctx.hasText) {
          return {
            id: "chooseAudio",
            l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
            englishLabel: ctx.textHasAudio ? "A Recording" : "None",
            subLabelL10nId: "EditTab.Image.PlayWhenTouched",
            featureName: "canvas",
            onSelect: async () => {},
            subMenuItems: [
              {
                id: "useTalkingBookTool",
                l10nId: "UseTalkingBookTool",
                englishLabel: "Use Talking Book Tool",
                featureName: "canvas",
                onSelect: async () => {
                  runtime.closeMenu(false);
                  // AudioRecording.showTalkingBookTool()
                },
              },
            ],
          };
        }

        return {
          id: "chooseAudio",
          l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
          englishLabel: "Choose...",
          subLabelL10nId: "EditTab.Image.PlayWhenTouched",
          featureName: "canvas",
          onSelect: async () => {},
          subMenuItems: [
            {
              id: "removeAudio",
              l10nId: "EditTab.Toolbox.DragActivity.None",
              englishLabel: "None",
              featureName: "canvas",
              onSelect: async (itemCtx) => {
                itemCtx.canvasElement.removeAttribute("data-sound");
                runtime.closeMenu(false);
              },
            },
            {
              id: "playCurrentAudio",
              l10nId: "ARecording",
              englishLabel: "A Recording",
              featureName: "canvas",
              availability: {
                visible: (itemCtx) => itemCtx.hasCurrentImageSound,
              },
              onSelect: async (_itemCtx) => {
                runtime.closeMenu(false);
                // playSound(currentSoundId)
              },
            },
            {
              id: "chooseAudio",
              l10nId: "EditTab.Toolbox.DragActivity.ChooseSound",
              englishLabel: "Choose...",
              featureName: "canvas",
              onSelect: async (_itemCtx) => {
                runtime.closeMenu(true);
                // showDialogToChooseSoundFileAsync()
              },
            },
            {
              kind: "help",
              helpRowL10nId: "EditTab.Toolbox.DragActivity.ChooseSound.Help",
              helpRowEnglish:
                "You can use elevenlabs.io to create sound effects if your book is non-commercial. Make sure to give credit to \"elevenlabs.io\".",
              separatorAbove: true,
            },
          ],
        };
      },
    },
  },
  linkGridChooseBooks: {
    kind: "command",
    id: "linkGridChooseBooks",
    l10nId: "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
    englishLabel: "Choose books...",
    icon: CogIcon,
    action: async (ctx, runtime) => {
      runtime.closeMenu(true);
      editLinkGrid(ctx.canvasElement);
    },
    toolbar: {
      render: (ctx, _runtime) => (
        <>
          <IconButton onClick={() => editLinkGrid(ctx.canvasElement)}>
            <CogIcon />
          </IconButton>
          <button onClick={() => editLinkGrid(ctx.canvasElement)}>
            Choose books...
          </button>
        </>
      ),
    },
  },
  duplicate: {
    kind: "command",
    id: "duplicate",
    featureName: "canvas",
    l10nId: "EditTab.Toolbox.CanvasTool.Duplicate",
    englishLabel: "Duplicate",
    icon: DuplicateIcon,
    action: async (ctx, _runtime) => {
      getCanvasElementManager()?.duplicateCanvasElement();
    },
  },
  delete: {
    kind: "command",
    id: "delete",
    featureName: "canvas",
    l10nId: "EditTab.Toolbox.CanvasTool.Delete",
    englishLabel: "Delete",
    icon: DeleteIcon,
    action: async (ctx, _runtime) => {
      getCanvasElementManager()?.deleteCanvasElement();
    },
  },
  // ... all other commands follow the same pattern
};
```

---

## Shared Availability Presets

Because `availabilityRules` is where all `visible`/`enabled` logic lives, related sets of rules are extracted into named preset objects. Element definitions compose them via spread. This is the primary mechanism for sharing behavior across element types.

```ts
// Type alias for convenience
export type AvailabilityRulesMap = ICanvasElementDefinition["availabilityRules"];

// Reused for surface-specific behavior (toolbar/menu/tool panel).
type SurfaceRule = {
  visible?: (ctx: IControlContext) => boolean;
  enabled?: (ctx: IControlContext) => boolean;
};

// --- Image-related commands ---
export const imageAvailabilityRules: AvailabilityRulesMap = {
  chooseImage:       { visible: (ctx) => ctx.hasImage, enabled: (ctx) => ctx.canModifyImage },
  pasteImage:        { visible: (ctx) => ctx.hasImage, enabled: (ctx) => ctx.canModifyImage },
  copyImage:         { visible: (ctx) => ctx.hasImage, enabled: (ctx) => ctx.hasRealImage },
  resetImage:        { visible: (ctx) => ctx.hasImage },
  // Parity note:
  // - toolbar: only show when metadata is missing
  // - menu: always show for modifiable image element, but disable for placeholder/no real image
  missingMetadata: {
    surfacePolicy: {
      toolbar: {
        visible: (ctx) => ctx.hasRealImage && ctx.missingMetadata,
      } as SurfaceRule,
      menu: {
        visible: (ctx) => ctx.hasImage && ctx.canModifyImage,
        enabled: (ctx) => ctx.hasRealImage,
      } as SurfaceRule,
    },
  },
  expandToFillSpace: { visible: (ctx) => ctx.isBackgroundImage },
};

// --- Whole-element commands (duplicate / delete) ---
export const wholeElementAvailabilityRules: AvailabilityRulesMap = {
  duplicate: {
    visible: (ctx) =>
      !ctx.isLinkGrid && !ctx.isBackgroundImage && !ctx.isSpecialGameElement,
  },
  delete: {
    surfacePolicy: {
      toolbar: {
        visible: (ctx) => !ctx.isLinkGrid && !ctx.isSpecialGameElement,
      } as SurfaceRule,
      menu: {
        visible: (ctx) => !ctx.isLinkGrid,
      } as SurfaceRule,
    },
    enabled: (ctx) => {
      if (ctx.isBackgroundImage) return ctx.hasRealImage;
      if (ctx.isSpecialGameElement) return false;
      return true;
    },
  },
  toggleDraggable: { visible: (ctx) => ctx.canToggleDraggability },
  togglePartOfRightAnswer: { visible: (ctx) => ctx.hasDraggableId },
};

// --- Video commands ---
export const videoAvailabilityRules: AvailabilityRulesMap = {
  chooseVideo: { visible: (ctx) => ctx.hasVideo },
  recordVideo: { visible: (ctx) => ctx.hasVideo },
  playVideoEarlier: { visible: (ctx) => ctx.hasVideo },
  playVideoLater: { visible: (ctx) => ctx.hasVideo },
};

// --- Audio commands (only in draggable games) ---
export const audioAvailabilityRules: AvailabilityRulesMap = {
  chooseAudio: { visible: (ctx) => ctx.canChooseAudioForElement },
};

// Note: submenu rows such as remove/current-sound/use-talking-book are
// modeled as dynamic `IControlMenuRow` rows within chooseAudio.menu.buildMenuItem,
// with optional `availability` per row.

// Audio submenu variants:
// - Image element variant:
//   1) "None"
//   2) Optional current-sound row (`playCurrentAudio`) when current sound exists
//   3) "Choose..."
//   4) Help row (`helpRowL10nId: EditTab.Toolbox.DragActivity.ChooseSound.Help`, `separatorAbove: true`)
// - Text element variant:
//   1) "Use Talking Book Tool"
//   (label reflects current state via `textHasAudio`, but row set is text-specific)

// --- Text and bubble commands ---
export const textAvailabilityRules: AvailabilityRulesMap = {
  format: { visible: (ctx) => ctx.hasText },
  copyText: { visible: (ctx) => ctx.hasText },
  pasteText: { visible: (ctx) => ctx.hasText },
  autoHeight: {
    visible: (ctx) => ctx.hasText && !ctx.isButton,
  },
  fillBackground: { visible: (ctx) => ctx.isRectangle },
};

export const bubbleAvailabilityRules: AvailabilityRulesMap = {
  addChildBubble: {
    visible: (ctx) => ctx.hasText && !ctx.isInDraggableGame,
  },
};
```

Presets are plain TypeScript objects—no magic, no framework. Adding a new preset is just adding a new exported constant in the same file.

---

## Element Type Definition

Each element type declares:
- **`menuSections`**: which sections appear in the right-click/`…` menu (auto-dividers between sections, in listed order).
- **`toolbar`**: the exact ordered list of commands (and spacers) for the context controls bar.
- **`toolPanel`**: which sections appear as controls in the `CanvasToolControls` side panel.
- **`availabilityRules`**: all `visible`/`enabled` logic for this element type, composed from shared presets plus any element-specific additions or exclusions.

```ts
export interface ICanvasElementDefinition {
  type: CanvasElementType;

  menuSections: SectionId[];
  toolbar: Array<ControlId | "spacer">;
  toolPanel: SectionId[];

  // visible/enabled logic for every command this element uses.
  // Compose from shared presets, then add element-specific policy entries.
  // Use "exclude" to hide a command that is present in a spread preset.
  availabilityRules: Partial<
    Record<
      ControlId,
      | "exclude"
      | {
          visible?: (ctx: IControlContext) => boolean;
          enabled?: (ctx: IControlContext) => boolean;
          surfacePolicy?: Partial<
            Record<
              "toolbar" | "menu" | "toolPanel",
              {
                visible?: (ctx: IControlContext) => boolean;
                enabled?: (ctx: IControlContext) => boolean;
              }
            >
          >;
        }
    >
  >;
}
```

### Rendering helpers

Three small helpers, one per surface:

```ts
// Returns the ordered toolbar items, spacers preserved, visible items only.
export function getToolbarItems(
  definition: ICanvasElementDefinition,
  ctx: IControlContext,
): Array<IResolvedControl | { id: "spacer" }>;

// Returns sections of filtered menu rows; renderer inserts dividers between sections.
export function getMenuSections(
  definition: ICanvasElementDefinition,
  ctx: IControlContext,
): IResolvedControl[][];

// Returns ordered tool-panel components for the visible commands.
export function getToolPanelControls(
  definition: ICanvasElementDefinition,
  ctx: IControlContext,
): Array<{
  Component: React.FunctionComponent<{ ctx: IControlContext; panelState: ICanvasToolsPanelState }>;
  ctx: IControlContext;
}>;
```

Each helper:
1. Iterates the element's section list for that surface and resolves controls from `section.controlsBySurface[surface]`.
2. Looks up `availabilityRules` for each command (`"exclude"` drops it; an object supplies `visible`/`enabled`).
3. Computes effective rules with precedence: `surfacePolicy[surface]` first, then base policy, then default (`visible: true`, `enabled: true`).
4. Returns only items where effective `visible(ctx)` is true.
5. For toolbar controls, if `control.toolbar.render` exists, render that node; otherwise render the standard icon-button shape.
6. For menu, inserts exactly one divider between non-empty sections automatically.

Menu rendering also supports optional keyboard shortcut display text on each menu row (from either `menu.shortcutDisplay` or `IControlMenuCommandRow.shortcut.display`).
The renderer places shortcut text in a right-aligned trailing area of each row.

Menu help rows (`kind: "help"`) render as non-clickable explanatory text and support localization via `helpRowL10nId`.

Menu rendering also resolves an effective `featureName` for each row:

1. `row.featureName` if present,
2. otherwise `control.featureName`.
3. if neither is present, render with no subscription gating/badge logic.

That value is passed to `LocalizableMenuItem.featureName` so existing subscription behavior applies (badge, disabled styling, click-through to subscription settings when unavailable).

Keyboard handling rule:

1. A menu item shortcut only triggers when its effective policy says it is visible and enabled.
2. Keyboard dispatch invokes and awaits the same `onSelect`/`action` path as pointer clicks.
3. Shortcuts are optional metadata. Commands without shortcut metadata remain fully valid.

---

## Example: Image Canvas Element

```ts
export const imageCanvasElementDefinition: ICanvasElementDefinition = {
  type: "image",
  menuSections: ["image", "audio", "wholeElement"],
  toolbar: [
    "missingMetadata",
    "chooseImage",
    "pasteImage",
    "expandToFillSpace",
    "spacer",
    "duplicate",
    "delete",
  ],
  toolPanel: [],
  availabilityRules: {
    ...imageAvailabilityRules,
    ...audioAvailabilityRules,
    ...wholeElementAvailabilityRules,
  },
};
```

**Toolbar** at runtime (items whose `visible` returns false are omitted):

```
missingMetadata?  chooseImage  pasteImage  expandToFillSpace?  ── spacer ──  duplicate?  delete
```

**Menu** at runtime (auto-dividers between sections):

```
── image section ──
  chooseImage / pasteImage / copyImage / missingMetadata / resetImage / expandToFillSpace?
── audio section ──
  chooseAudio (submenu rows include remove/current-sound/use-talking-book as applicable)
── wholeElement section ──
  duplicate? / delete / toggleDraggable? / togglePartOfRightAnswer?
```

**Tool panel**: empty → `CanvasToolControls` shows `noControlsSection`. No `switch` statement needed.

---

## Example: Speech/Caption Canvas Element

```ts
export const speechCanvasElementDefinition: ICanvasElementDefinition = {
  type: "speech",
  menuSections: ["audio", "bubble", "text", "wholeElement"],
  toolbar: ["format", "spacer", "duplicate", "delete"],
  toolPanel: ["bubble", "text"],
  availabilityRules: {
    ...audioAvailabilityRules,
    ...bubbleAvailabilityRules,
    ...textAvailabilityRules,
    ...wholeElementAvailabilityRules,
  },
};
```

The side panel for `speech` gets the bubble controls (style, tail, rounded corners, outline color) and text controls (text color, background color) from `getToolPanelControls`. The old `switch (canvasElementType)` is gone.

---

## Example: Navigation Image Button

Reuses shared image/text/whole-element presets and applies a small surface-specific policy rule for `missingMetadata`:

```ts
export const navigationImageButtonDefinition: ICanvasElementDefinition = {
  type: "navigation-image-button",
  menuSections: ["url", "image", "wholeElement"],
  toolbar: [
    "setDestination",
    "chooseImage",
    "pasteImage",
    "spacer",
    "duplicate",
    "delete",
  ],
  // Keep parity with current CanvasToolControls button behavior:
  // text color (if label), background color, image fill (if image present).
  toolPanel: ["text", "imagePanel"],
  availabilityRules: {
    ...imageAvailabilityRules,
    imageFillMode: { visible: (ctx) => ctx.hasImage },
    ...textAvailabilityRules,
    ...wholeElementAvailabilityRules,
    // Keep menu availability while preserving toolbar behavior.
    missingMetadata: {
      surfacePolicy: {
        toolbar: { visible: () => false },
        menu: { visible: (ctx) => ctx.hasImage && ctx.canModifyImage },
      },
    },
    setDestination: { visible: () => true },
    textColor: { visible: (ctx) => ctx.hasText },
    backgroundColor: { visible: () => true },
    // The tool-panel image-fill control is only meaningful when image exists.
    // Background-only expand command remains governed by imageAvailabilityRules.
  },
};
```

Reading this file tells you everything about how this element behaves: no cross-referencing control definitions required.

---

## Example: Book Link Grid

```ts
export const bookLinkGridDefinition: ICanvasElementDefinition = {
  type: "book-link-grid",
  menuSections: ["linkGrid"],
  toolbar: ["linkGridChooseBooks"],
  toolPanel: ["text"],
  availabilityRules: {
    linkGridChooseBooks: { visible: (ctx) => ctx.isLinkGrid },
    textColor: "exclude",
    backgroundColor: { visible: (ctx) => ctx.isBookGrid },
  },
};
```

This keeps link-grid command mapping explicit and avoids relying on incidental text-section wiring.

---

## Example: Adding a New Command

Suppose we add "Crop Image":

1. Add `"cropImage"` to `ControlId`.
2. Add its control definition to `controlRegistry` (icon + label + action only):

```ts
cropImage: {
  id: "cropImage",
  l10nId: "EditTab.Image.Crop",
  englishLabel: "Crop Image",
  icon: CropIcon,
  action: async (ctx, runtime) => {
    runtime.closeMenu(true);
    launchCropDialog(ctx.canvasElement);
  },
},
```

3. Add `"cropImage"` to `controlSections.image.controlsBySurface.menu`.
4. Add its `visible`/`enabled` policy to `imageAvailabilityRules`:

```ts
export const imageAvailabilityRules: AvailabilityRulesMap = {
  // ... existing entries ...
  cropImage: { visible: (ctx) => ctx.hasImage && ctx.canModifyImage },
};
```

All element types that spread `imageAvailabilityRules` automatically get the correct visibility for `cropImage`. Elements with an explicit `toolbar` list must add `"cropImage"` explicitly—the menu auto-grows from sections, but the toolbar order is always intentional.

---

## Example: Special Case—No Duplicate for Background Image

The suppress logic lives in `wholeElementAvailabilityRules`, which every relevant element spreads:

```ts
export const wholeElementAvailabilityRules: AvailabilityRulesMap = {
  duplicate: {
    visible: (ctx) =>
      !ctx.isLinkGrid && !ctx.isBackgroundImage && !ctx.isSpecialGameElement,
  },
  // ...
};
```

Change it here and every element that spreads `wholeElementAvailabilityRules` picks it up automatically.

---

## CanvasToolControls Integration

```tsx
const controls = getToolPanelControls(
  canvasElementDefinitions[canvasElementType],
  ctx,
);

return (
  <div>
    {controls.map(({ Component, ctx: cmdCtx }, i) => (
      <Component key={i} ctx={cmdCtx} panelState={panelState} />
    ))}
  </div>
);
```

The `switch` on `canvasElementType` is gone. The side-panel controls for style, tail, rounded corners, color pickers, and image fill mode are each backed by a control definition with a `canvasToolsControl` renderer. Element types opt in by listing the relevant section in `toolPanel`.

Two parity constraints are explicit in this design:

1. **Page-level gate first**: keep the existing `CanvasTool.isCurrentPageABloomGame()` behavior that disables the whole options region on game pages.
2. **Capability-gated panel controls**: button/book-grid behavior is driven by `IControlContext` flags (`isButton`, `isBookGrid`, `hasImage`, `hasText`), not by a hard-coded `switch`.

---

## Toolbar Spacers

Spacers are listed explicitly in `toolbar` as `"spacer"`, just like a command id. The toolbar renderer skips leading/trailing spacers and collapses consecutive ones—exactly the current `normalizeToolbarItems` behavior—but that normalization stays in the renderer, not in the element definition.

## Menu Dividers and Help Rows

- Section dividers are automatic. The renderer inserts exactly one divider between non-empty menu sections.
- Section definitions and command builders never declare divider rows for section boundaries.
- For explanatory non-clickable content, use `IControlMenuHelpRow` with `helpRowL10nId`.
- For submenu-only visual separation, use `separatorAbove: true` on the row that needs separation.

### Renderer acceptance criteria (`IControlMenuHelpRow`)

- `helpRowL10nId` is required and is the primary localized text source; `helpRowEnglish` is fallback.
- Help rows render as non-clickable content (no command invocation, no command hover/active behavior).
- Help rows are not keyboard-command targets.
- `separatorAbove: true` inserts one separator directly above that help row in the same submenu.
- `availability.visible(ctx) === false` omits the help row.
- Help rows do not participate in `featureName` gating/badge logic.

## Composite Toolbar Controls

Most controls render as icon buttons, but some controls need richer toolbar UI.
Use `toolbar.render` for those cases.

Example (`linkGridChooseBooks` style behavior):

```ts
linkGridChooseBooks: {
  kind: "command",
  id: "linkGridChooseBooks",
  l10nId: "EditTab.Toolbox.CanvasTool.LinkGrid.ChooseBooks",
  englishLabel: "Choose books...",
  icon: CogIcon,
  action: async () => {}, // no-op; toolbar.render handles interaction
  toolbar: {
    render: (ctx, _runtime) => (
      <>
        <IconButton onClick={() => editLinkGrid(ctx.canvasElement)}>
          <CogIcon />
        </IconButton>
        <button onClick={() => editLinkGrid(ctx.canvasElement)}>
          Choose books...
        </button>
      </>
    ),
  },
},
```

Use this escape hatch sparingly; prefer standard icon-button controls where possible.

---

## Unknown/Unregistered Type Fallback

Keep the current graceful behavior:

1. If inference returns `undefined`, warn and fall back to `"none"`.
2. If inference returns a value missing from the definitions registry, warn and fall back to `"none"`.
3. Keep a `none` definition in `canvasElementDefinitions` with conservative controls (`wholeElement` section + duplicate/delete rules).

This preserves compatibility with books produced by newer Bloom versions.

---

## Migration Path

Migrate in phases to preserve behavior and reduce regressions:

1. **Parity inventory phase**
  - Lock a checklist of all current controls/conditions (menu, toolbar, tool panel).
  - Add/update e2e assertions for high-risk behaviors (audio nested menu, draggability toggles, nav button panel controls).
2. **Dual-path implementation phase**
  - Introduce new registry/helper modules while keeping existing rendering path in place.
  - Add a temporary adapter that can render from either path in dev/test builds.
3. **Cutover phase**
  - Switch `CanvasElementContextControls` and `CanvasToolControls` to new helpers.
  - Remove old command-construction code only after parity tests pass.
4. **Cleanup phase**
  - Delete dead code, keep docs updated, keep runtime fallback-to-`none` behavior.

### Adapter focus-lifecycle test checklist (must pass before cutover)

- Opening menu from toolbar (`mousedown` + `mouseup`) does not steal/edit-focus unexpectedly.
- Right-click menu opens at anchor position and preserves current selection behavior.
- Closing menu without dialog restores focus-change handling normally.
- Closing menu with `closeMenu(true)` preserves current launching-dialog skip-focus-change semantics.
- Menu keyboard activation path executes the same command runtime and focus behavior as pointer activation.
- Help rows are skipped by command keyboard dispatch.

---

## Required Parity Behaviors

Before removing legacy control-building code, confirm the new system maps all of these:

- **Video menu**: `playVideoEarlier` / `playVideoLater` enablement tied to previous/next video containers.
- **Image menu**: `copyImage` and `resetImage` with current disabled rules.
- **Rectangle text menu**: `fillBackground` toggles `bloom-theme-background`.
- **Bubble section**: `addChildBubble` hidden in draggable games.
- **Text menu**: `copyText`, `pasteText`, and `autoHeight` (`autoHeight` hidden for button elements).
- **Whole-element menu**: `toggleDraggable` and `togglePartOfRightAnswer` with current game-specific constraints.
- **Audio menu**: nested submenu behavior for image/text variants, including `useTalkingBookTool` and dynamic current-sound row.
- **Link-grid mapping**: `linkGridChooseBooks` appears in toolbar/menu for book-link-grid and nowhere else.
- **Menu lifecycle**: keep close-menu + focus behavior for dialog-launching commands.
- **Parity row — menu focus lifecycle**: verify open/close preserves current focus semantics, including launching-dialog behavior.
- **Parity row — audio help row**: verify localized help row renders in audio submenu, is non-clickable, and respects `separatorAbove`.
- **Tool panel parity**: support button/book-grid capability-driven control sets and game-page disable gate.

---

## Example: Adding a New Tool Panel Control

Suppose we add a "Letter Spacing" slider to the text panel:

1. Add `"letterSpacing"` to `ControlId`.
2. Add its control definition to `controlRegistry`:

```ts
letterSpacing: {
  kind: "panel",
  id: "letterSpacing",
  l10nId: "EditTab.Toolbox.CanvasTool.LetterSpacing",
  englishLabel: "Letter Spacing",
  tooltipL10nId: "EditTab.Toolbox.CanvasTool.LetterSpacingTooltip",
  // No icon — this is a slider, not a button.
  canvasToolsControl: LetterSpacingControl,
},
```

3. Add `"letterSpacing"` to `controlSections.text.controlsBySurface.toolPanel`.
4. Add a visibility policy entry to `textAvailabilityRules` (or define it inline on the element):

```ts
export const textAvailabilityRules: AvailabilityRulesMap = {
  // ... existing entries ...
  letterSpacing: { visible: (ctx) => ctx.hasText },
};
```

5. Write the `LetterSpacingControl` component:

```tsx
export const LetterSpacingControl: React.FunctionComponent<{
  ctx: IControlContext;
  panelState: ICanvasToolsPanelState;
}> = (props) => {
  // Can use hooks freely — this is a component reference, not a render function
  const [value, setValue] = React.useState(0);
  return <Slider value={value} onChange={(_, v) => setValue(v as number)} />;
};
```

Element types that include `"text"` in their `toolPanel` array get the new control automatically. No switch statement, no per-element changes.

---

## IControlContext Scope

`IControlContext` contains mostly boolean facts plus a small set of simple derived values (for example async-derived booleans that may be `undefined` while loading) — everything needed by `visible`/`enabled` callbacks — but no pre-computed DOM references. Action callbacks query the DOM directly from `canvasElement` when they need it.

**Rationale:** `visible`/`enabled` callbacks live in element `availabilityRules` and shared presets; they are called on every render by the filtering helpers. Giving them a clean, named set of boolean flags keeps those callbacks readable and the hot path free of DOM coupling. Action callbacks fire once on user interaction, so an inline `getElementsByClassName` call there is fine and keeps the context interface from growing unboundedly.

The rule is: **if a fact drives visibility or enabled state, it belongs in `IControlContext`; if it is only needed when an action fires, derive it inside the action from `ctx.canvasElement`.**

New flags may be added to `IControlContext` as needed, but only if they are actually referenced by a `visible` or `enabled` callback. All DOM querying for context construction is isolated in one `buildCommandContext` function, so the coupling is contained.

---

## Finalized Interaction Rules

- Nested audio menus use one-level `subMenuItems` on `IControlMenuRow`.
- Menu supports command rows and non-clickable help rows (`kind: "help"` with `helpRowL10nId`).
- Menu section dividers are automatic and never declared as rows.
- Menu rows may include optional keyboard `shortcut` metadata; shortcut dispatch executes the same path as clicking the row.
- Menu-close/dialog-launch behavior stays in command handlers via `runtime.closeMenu(launchingDialog?)`.
- Command `action` and menu-row `onSelect` are async (`Promise<void>`), while `menu.buildMenuItem` remains synchronous.
- Async-derived context facts use `boolean | undefined` (`undefined` while loading), including `textHasAudio`.
- Anchor/focus lifecycle ownership remains in renderer/adapter code; command runtime stays minimal.
- Control definitions use discriminated union kinds: `kind: "command"` and `kind: "panel"`.

---

## TODO

- Update e2e matrix/tests to validate section auto-divider behavior between non-empty sections.

