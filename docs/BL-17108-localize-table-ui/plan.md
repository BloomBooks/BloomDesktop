# BL-17108: localize everything the user sees in tables

## What the survey found

**bloom-table library (v0.1.2).** Every string is English, and the host can translate only the
React `CellMenuItems`, through its `localize?: (englishLabel, id) => string` prop. The library's
own right-click popup (`buildCellSection`) mounts `CellMenuItems` without that prop, so even
those items are English in Bloom. The strings that Bloom users see:

- On-canvas chrome, built once on the first `attachTable` and kept: the Row, Column and Table
  pill tooltips; the two + buttons ("Add column at the right edge", "Add row at the bottom
  edge").
- The paint format badge ("End format painting (Esc)").
- The Row, Column and Table menus, rebuilt on every open: headings, commands, the info note
  "Right click on a cell for Cell menu", the gap sliders, Copy/Paste properties, Copy/Cut/Delete
  Table.
- The Size control (Grow, Hug, Fixed size, `mm`, Size).
- Content Type and its options (Text, Table, Image, Video, taken from each type's
  `englishName`).
- The Format section: alignment and vertical alignment toggles, Padding (built by English
  concatenation, "Padding between border and ${what}"), Fill, Corners ("Corner radius ${n}"),
  the border scope tabs (All, Outer, Inner, Border Brush) with their sixteen captions, Border
  color, Border Style (None, Solid, Dashed, Dotted, Double), Border Weight ("0 (None)").
- The Cell menu items (Table Cell, Paint format, Merge with cell to the right, Merge with cell
  below, Split).

Not seen by Bloom users, so left out of this card:

- The React `TableMenu` sidebar panel and its `BorderControl` menus: only the library's demo
  mounts them.
- Undo step names (`tableHistoryUpdated`'s `operation`): Bloom's `tableUndo.ts` keeps them as
  English labels and uses them to recognise "Clear History", "Detach Table" and "Undo "/"Redo ",
  and no Bloom UI shows them yet. This matches the decision that page-history undo step names
  get no XLF entries until the Undo button shows them.
- The `operationDetail` strings, which are debug text.
- The content templates' "Placeholder Image" alt text and video fallback text: Bloom replaces
  every built-in template with its own (`replaceCellContentType` in `tableEditing.ts`).

**Bloom.** Its own table strings are localized already (the Canvas tool's Table item,
`EditTab.CustomPage.Table` in Change Layout, the subscription tier tooltip, the Experimental
"Tables" checkbox). One gap: `FeatureStatus.cs` asks for `Feature.Table` with
`GetDynamicString`, and no XLF file has that id, so the Publish tab's "uses the "{1}" feature"
notice says "Table" in English. Restructuring is gated silently (the chrome is withheld), so
there are no gating messages to translate. The Languages item (`Common.Languages`) belongs to
BL-17105 and is not on this branch.

## The change

### In bloom-table (own worktree `D:/bloom-table.worktrees/BL-17108`, branch `BL-17108-localize`)

1. **One list of every visible string.** A new module, `ui-strings.ts`, exports
   `tableUiStrings`: an object from a string id to its English text, e.g.
   `addRowAbove: "Add Row Above"`, `scopeCaption.outer.row: "The outside edges of this row"`,
   `cornerRadius: "Corner radius {0}"`. It is exported so a host can preload translations for
   every id. Strings built by concatenation become whole phrases, one id each (padding for
   text, image, video and table; the scope captions per scope and target).
2. **A host localizer.** `setTableLocalizer(fn: (englishLabel: string, id: string) => string)`,
   a module-level setter like `setCellMenuItemFilter`, with the same argument order as
   `CellMenuItems`'s existing `localize` prop so a host writes one function for both. Inside
   the library, a helper `uiString(id, ...args)` looks up the English, asks the localizer, and
   fills `{0}`-style placeholders.
3. **Every DOM builder uses `uiString`**: `table-size-buttons.ts`, `menu-widgets.ts`,
   `paint-format.ts`, and the content type option labels (by id `contentType:<typeId>`, which
   `CellMenuItems` already uses; a host-registered type with no entry keeps its
   `englishName`).
4. **Calling `setTableLocalizer` relabels the chrome that already exists** (pills, + buttons),
   because those are built once on the first attach and Bloom's translations arrive
   asynchronously.
5. **`buildCellSection` passes the localizer to `CellMenuItems`**, and `CellMenuItems` falls
   back to it when its own `localize` prop is absent.
6. **`setColorInputValue` stops finding the border colour input by its aria-label**, which would
   break once that label is translated; it uses a `data-` attribute instead.
7. Tests: one that sets a localizer which marks every string (e.g. wraps it in `[...]`), opens
   each menu, and fails on any visible text, title or aria-label that came through unmarked.
   That test is what keeps a future string from slipping past. Existing tests keep passing
   because the default localizer returns the English.
8. Released as `dist-v0.1.3` (bloom-table master 6a4d145).

### In Bloom (this branch)

1. A new `tableUiLocalization.ts` beside `tableEditing.ts`: in `SetupTableEditing`, preload
   every id in `tableUiStrings` as `EditTab.Table.<id>` with
   `theOneLocalizationManager.loadStringsPromise`, then call `setTableLocalizer` with a
   function that returns `theOneLocalizationManager.getText("EditTab.Table." + id, english)`.
   Until the preload returns, menus show English; menus opened after that are translated, and
   the pills are relabelled.
2. XLF entries for the 89 ids in `tableUiStrings`, each with a translator note, plus
   `Feature.Table`; `EditTab.Toolbox.CanvasTool.Table` and `EditTab.CustomPage.Table` lose
   `translate="no"`. These went to master on their own (cbf5b1603b) so that Crowdin has them,
   and French, Spanish, Portuguese and Indonesian suggestions plus 31 tagged screenshots
   (`Tables/…`) are on Crowdin as SILCrowdinBot, unapproved.
3. A unit test that every id in `tableUiStrings` has an `EditTab.Table.<id>` entry in the
   English XLF, so a string added to the library later fails Bloom's tests until it gets one.
4. Bump the `bloom-table` pin to `dist-v0.1.3`.

## Existing tests whose expectations change

Two tests in bloom-table's `CellMenuItems.test.tsx` match their test localizer on the new
string ids (`CellMenu.Heading`, `CellMenu.Split`) instead of the model's item ids; their
assertions are unchanged. Every other label test still reads English, which is what it gets.

## Overlap with BL-17105

BL-17105's bloom-table work (`host-menu-sections`) changes `table-size-buttons.ts`,
`CellMenuItems.tsx` and `index.tsx`, which this card also changed, so it rebases onto
bloom-table master 6a4d145. Any string it adds to the library's own UI goes into
`tableUiStrings` and through `uiString`, and needs an `EditTab.Table.<id>` XLF entry here.
