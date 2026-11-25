# Insert Table: a table in the text of a text box

A user can put a table in the middle of a text box's text, the way a word processor does. Today a
table can only fill a section of the page (the Table link in page layout) or sit in a canvas
element. Inline images ("Insert Image") already go inside a text box, but they are placed by
measurement, not by a position in the text. A table inserted this way is always anchored to the
text: it sits between two paragraphs and moves when text is added above it.

## What the user sees

- Right-clicking in an ordinary text box offers **Insert Table** on the text context menu, after
  Insert Image. It is not offered in a text box inside a canvas element, inside a table cell, or
  in a `data-book` box (title, credits and other boxes whose text is stored in the book's data).
- Insert Table splits the paragraph at the caret and puts a new two-by-two table between the two
  halves. A caret at the start or end of a paragraph puts the table before or after that
  paragraph, with no empty paragraph left behind.
- The table is on its own lines: the text stops above it and starts again below it. Text never
  wraps beside it. It is the width of the text box.
- Its rows grow with their text, like a table made with the Table link in page layout. A table
  that makes the text box too tall shows as overflow, as too much text does today.
- The space above and below the table is half a line of the surrounding text each (`0.5lh`), so the
  table adds exactly one line of space, whatever the style's font size and line spacing. Above the
  table, that space overlaps the style's paragraph spacing, and the larger of the two wins.
- A table that ends the text has an empty paragraph after it, so that the user can type below it.
  Until something is typed there, the overflow check leaves that paragraph out, so a table that
  reaches the bottom of the box is not reported as overflowing because of an empty line.
- The table belongs to the language whose text it was inserted into, and appears once. The other
  languages' text in the same box has no table. If the book stops showing that language, the
  table is hidden along with its text.
- Its text cells show the same languages that a new table cell gets anywhere else (see
  `docs/table-languages/README.md`).
- The text in its cells cannot be formatted: the cells' text boxes have no CKEditor, so there is
  no bold, italic or format toolbar there. The text around the table keeps its formatting.
- All of the table's own menus, sizes and formats work as they do for any other table, and the
  table is gated by the same subscription feature (`table`).
- The table's buttons (add a row or column, and the table, row and column menus) show only while
  the table is selected, that is, while the focus is in one of its cells. They sit just outside
  the table, over the lines of text above and below it, so showing them whenever the pointer comes
  near would put them in the way of clicking that text.
- The user can drag the table to another place in the same text, by a grip that shows just left
  of the table's top-left corner while the table is selected. While dragging, a horizontal line
  shows where it will land at the start or end of a paragraph, and a text-cursor mark shows where
  a drop inside a paragraph would split it, as Insert Table does. A drag cannot take the table to
  another text box or another language, or into a table.
- Backspace at the start of the paragraph after the table, or Delete at the end of the paragraph
  before it, does not delete or merge into the table. The table is deleted through its own Table
  menu. Ctrl+A then Delete in the surrounding text leaves the table in place.
- Insert, drag and delete are each one step on Bloom's undo stack.

## What is stored

```html
<div class="bloom-translationGroup" data-default-languages="V">
  <div class="bloom-editable bloom-content1" lang="tpi" contenteditable="true">
    <p>Text above the table.</p>
    <div class="bloom-table bloom-tableInText bloom-preventRemoval" contenteditable="false" tabindex="0" ...>
      <!-- rows and cells as bloom-table makes them; each text cell holds its own
           bloom-translationGroup with one bloom-editable per language -->
    </div>
    <p>Text below the table.</p>
  </div>
  <div class="bloom-editable bloom-content2" lang="en" contenteditable="true">
    <p>The English text has no table.</p>
  </div>
</div>
```

- `contenteditable="false"` on the table makes the browser and our own code treat it as one
  unit in the surrounding text, as it does for an inline image's wrapper. The table library makes
  each cell's editables `contenteditable="true"`, so the cells stay editable.
- `bloom-preventRemoval` is the class that `BloomField.ts` already uses to stop Backspace, Delete
  and Ctrl+A from removing an inline image.
- `bloom-tableInText` is new. Layout CSS uses it for the full-width block layout, and code that
  must skip a table in text (below) selects on it.
- The table is always a direct child of the `bloom-editable`, between `<p>` elements, never inside
  a `<p>`. The editable always has a `<p>` before and after the table, so the caret can reach
  both sides.

## Code that has to change

No code today puts text boxes inside another text box's text. A survey of the code that reads or
rewrites a text box's contents found these places, which assume that a box's descendants are all
its own text. Most of them need the same change: look only at the box's direct children, or skip
`.bloom-tableInText` and everything inside it. The plan is one shared helper for each language
(`isInTableInText(element)` in TypeScript and an XPath predicate in C#) rather than a different
check in each place.

### Changes that are needed to avoid losing content

| Where | What goes wrong | Change |
| --- | --- | --- |
| `bloomEditing.ts:891-929`, the reordering of a group's boxes by `bloom-content1/2/3` at page load | `find("div.bloom-editable")` also finds the cell editables, and reordering moves them out of the table | Only direct children (`:scope >`) |
| `editableDivUtils.ts:410-438`, `copyCkEditorDataToClone` | Pairs live and saved editables by index. Writing the outer box's data replaces the saved table, so the cells' own data goes into detached nodes and is not saved | Write the outer box with the table kept as it is, then the cells, or pair by element rather than by index |
| CKEditor on the outer box (`attachToCkEditor`, `bloomEditing.ts:1408`) | CKEditor 4 does not support editors inside an editor. Its undo snapshots and Enter handling would include the table | See the open question below |
| Code that sets a box's `innerHTML` while editing: `doCkEditorCleanup` (`editableDivUtils.ts:362`), reader tools undo and redo (`readerToolsModel.ts:729, 764`), `cleanUpNbsps` (`pageEditingMarkup.ts:677`), comment removal (`pageEditingMarkup.ts:732`), `showInvisibles.ts:110, 189` | Replaces the live table, which loses the cells' editors and the table library's state | Keep the live table element across the rewrite, as `replaceContentKeepingLiveInlineImages` (`editableDivUtils.ts:519`) does for inline images |
| Talking Book (`audioRecording.ts` 588-618, 682, 722-731, 3828-3916, 3975) | Splits the cells' text into the outer box's sentences, plays the cells' sentences twice, and in Text Box mode removes the cells' sentence spans and their recordings | Skip `contenteditable="false"` descendants when splitting (inline images already rely on this at 3901) and leave out the table when reading the outer box's audio and text |
| Spreadsheet export and import (`SpreadsheetExporter.cs:187, 408`, `SpreadsheetImporter.cs:1871, 2034`) | Exports the cells' text twice, and an import replaces the outer box's contents, table included | Exclude the table from the outer box's row, and keep it when importing the box's text. How the cells themselves go into a spreadsheet follows whatever tables elsewhere on a page already do |

### Changes for behaviour that would be wrong

| Where | What goes wrong | Change |
| --- | --- | --- |
| Decodable and Leveled reader (`readerToolsModel.ts:685`, `jquery.text-markup.ts:159-182`) | Counts the L1 cells' words twice, and other languages' cell words too | Skip the table when checking the outer box |
| `BloomField.ts` (1137, 1154, 891, 870, 783) | Backspace and Delete can merge into the table. Ctrl+A then Delete does not restore a paragraph. The caret can be moved into a cell | `bloom-preventRemoval` covers Backspace and Delete. The other checks look only at direct children. Make sure there is a `<p>` after a final table |
| Source bubbles (`BloomSourceBubbles.tsx:113-158`) | The table's divs become source text and its cell editables become extra tabs | Remove the table from the clone that the bubble shows |
| `TranslationGroupManager.cs:309`, `UpdateGeneratedClassesOnChildren` | The outer group's language settings are applied to the cell editables. This only comes out right because the cell groups are processed later | Direct children only |
| Adding a language to a box (`TranslationGroupManager.cs:1129-1216`) | The new language's box is cloned from the first box, so it gets a copy of the table with empty cells | Remove any `bloom-tableInText` from the clone |
| `FixDuplicateLanguageDivs` (`TranslationGroupManager.cs:918`) | Counts the table's text as the box's text | Leave out the table, as the inline-image exception at 988-1024 does |
| Motion duration (`Book.cs:5878`) and `SelectChildNarrationAudioElements` (`HtmlDom.cs:3156`) | Counts the cells' audio twice, and keeps audio for excluded languages when publishing | Leave out audio inside the table when reading the outer box |
| Overflow (`OverflowChecker.ts:34-38, 64`) | The cell editables count toward the limit of 30 boxes on a page that turns checking off | Count a table in text as part of its outer box |
| Placeholders and font sizes (`bloomEditing.ts:488, 601, 886`) | The outer box's placeholder and font size reach the cell editables | Direct children only |

### Things that need no change

Code that already looks only at a box's direct children (`getEditables`, `RemoveOtherLanguages`,
`FixDivOrdering`, the spreadsheet exporter's `WriteTranslationGroup`), code that finds the
nearest editable to the caret (the style editor, `pageEditingMarkup.ts:389`), thumbnails (made
from the rendered page), keyboarding, and character style clean-up.

## New code

- **Insert Table command.** Added to `getTextContextMenuContent` beside Insert Image, gated the
  same way as the Table link in page layout (`createTableSelector` in `origami.ts`). It finds the
  caret's paragraph, splits it with a Range, inserts the table markup and attaches it with
  `AttachNewTable`.
- **Layout CSS** for `.bloom-tableInText` in `content/bookLayout`, so a table in text lays out
  the same in Bloom, Bloom Player, ePUB and PDF: a full-width block, with no float.
- **Drag to move.** A handle on the table while it is selected (the table library already shows
  pills on a selected table). Dragging shows a line at the nearest paragraph boundary and moves the
  element there on drop.
- **Undo.** Insert, move and delete as steps on the undo stack, alongside the table operations
  from BL-16900 (`tableUndo.ts`).

## Tests

- Unit tests for the insert command (where the table lands for a caret at the start, middle and
  end of a paragraph, and at the end of the box), the new context-menu item and when it is
  offered, and dragging to a paragraph boundary.
- A unit or component test for each change in the table "Changes that are needed to avoid
  losing content": each one sets up a box with a table in its text, runs the code, and checks
  that the table and its cells' text are unchanged.
- C# tests for the `TranslationGroupManager`, spreadsheet and audio changes.
- One e2e test: insert a table in a text box, type in a cell and below the table, change page and
  come back, and check that both texts survived.

## What a trial in a running Bloom showed

A table made with the Table link in page layout was moved into a text box's text in the page's
DOM (no user action can do this yet), then saved and reloaded.

- **Load pulled the cells' text boxes out of the table.** The reordering step at
  `bloomEditing.ts:891` did it, as the survey predicted. Limiting it to the group's own text boxes
  (`children` instead of `find`) fixed it; that change is in the working tree.
- **Typing in a cell did nothing.** `jquery.longpress.js` is attached to the cell's text box and to
  the outer one, and keeps the last key pressed in one variable that both share. A keydown from the
  cell bubbles to the outer box, whose handler sees the same key again, takes it for a key being
  held down, and cancels it (`jquery.longpress.js:243`). Stopping key events at the table's edge
  fixed typing, Enter included.
- **The table took the whole height of the text box**, from `.bloom-table { height: 100% }` in the
  library's CSS, and pushed the text below it out of sight. `.bloom-tableInText` needs
  `height: auto`.
- **CKEditor loses what is typed in cells.** Each cell's text box gets its CKEditor, and then the
  outer box's CKEditor, when it becomes ready, rewrites the outer box's contents, table included.
  The twelve cell editors are left attached to copies that are no longer on the page, so the live
  cells have no editor. Typing in a cell shows on screen, but the save reads the cells from those
  detached editors: "Cell B new", typed in a live cell, was not in the saved file. Text typed in
  the cell before the reload, when the table was still in its own section, was kept.
- Backspace and Enter in the paragraphs beside the table were not tested reliably: the table's
  height had hidden the paragraph below it at the time.

## CKEditor

The outer text box keeps its CKEditor; the cells' text boxes get none (`attachToCkEditor` skips
them), so their text cannot be formatted. CKEditor 4 does not support an editor inside another
editor's box, and the trial showed that two levels of editor lose the cells' text on save. When
the outer editor loads it rewrites its box, so `keepLiveTablesWhenCkEditorLoads` puts the live
table back in place of the editor's copy, and `replaceContentKeepingLiveImagesAndTables` does the
same whenever other code rewrites the box.

## TODO

- **Talking Book reading order.** Talking Book plays the whole outer box (the text above and below
  the table), then the table's cells. A reader would expect the text above, the table, then the
  text below. In Text Box mode the outer box is one recording, so the table cannot come in the
  middle of it. Insert Table can ship without this.
- **Undo.** Insert Table is not yet a step on the undo stack, and typing in a cell cannot be undone
  (the cells have no CKEditor). CKEditor's own undo for typing in the outer box restores a copy of
  the box, which would rebuild the table; that is untested.
- **Spreadsheet export and import.** Import replaces a box's content, table included.
