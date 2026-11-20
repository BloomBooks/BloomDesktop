# Choosing the languages of a table's text cells

The user can choose which of the book's languages each text cell of a table shows. The choice is
made in the table's own menus, at the level of a cell, a row, a column or the whole table.

## What the user sees

Each of the Cell, Row, Column and Table menus gets a **Languages** item, directly after
Content Type and before Format: it is about what the cells hold, like Content Type, rather than
how they look. Pointing at it opens a submenu of check boxes:

```
Languages
  [x] L1 - Tok Pisin
  [x] L2 - English
  [ ] L3 - Spanish
```

- One check box per role the book has: L1, then L2 and L3 when the collection has them. L1 is
  the first language ticked in the toolbar, which is usually the collection's first language but
  need not be; L2 and L3 are the collection's second and third. If L2 or L3 is the same language
  as L1, only one entry is shown (the canvas element's Language submenu already drops duplicates
  this way, in `canvasControlTextMenuItems.ts`).
- Each entry's tooltip is the Collection Settings label for that language.
- At least one box stays ticked: a box is disabled when unticking it would leave some cell with
  no language.
- In the Row, Column and Table menus a box is ticked when every text cell covered shows that
  language, empty when none does, and shows a dash when only some do. Clicking a dash ticks that
  language for all of them.
- The item is hidden when the cell, or every cell the menu covers, is an image or video cell.
- A change is one of the table library's operations, so it is one step on Bloom's undo stack, and
  Undo puts the table back as it was, as for any other table operation. Like those, it has no
  Redo until typing joins the undo stack.

## What is stored

The ticked roles go into `data-default-languages` on the cell's `bloom-translationGroup`, as the
roles Bloom already uses: `V` for L1, `N1` for L2, `N2` for L3, or a list such as `V,N1`. They
mean here what they mean in every translation group (`ShouldNormallyShowEditable` in
`TranslationGroupManager.cs`): `V` is the first language ticked in the toolbar, and `N1` and `N2`
are the collection's second and third languages, whether or not the toolbar ticks them.
Storing roles rather than language tags means a cell keeps its role when the book is translated in
another collection: a word-list column set to L1 shows the new collection's L1.

Bloom's C# already reads a comma list in this attribute and shows the languages it names, so the
feature needs no change to the C#.

## Defaults and the toolbar

- A new text cell shows L1 only.
- A table text cell with no `data-default-languages` (every cell of a table made before this
  feature) shows the languages ticked in the toolbar, as any translation group with no setting
  does, and its menu ticks those.
- Turning a language on or off in the toolbar changes what a cell set to `V` shows only when it
  changes which language is ticked first. It does not change what a cell set to `N1` or `N2`
  shows.

## Setting a value at a higher level

There is no inheritance between levels. As with the Table menu's Fill, choosing in the Row,
Column or Table menu writes the value onto every text cell that menu covers. Image and video cells
are skipped.

A row or column added beside an existing one copies the languages of the cells it was inserted
beside, as it already copies fill, alignment, padding and corners. Paint format and
Copy / Paste properties carry the languages along with the other settings.

## Books where tables may not be restructured

When the subscription is below Pro, or the table feature is off, Bloom withholds the Cell menu and
the row, column and table pills (`tableEditing.ts`, `tablesMayBeRestructured`). The Languages
item goes with them, and there is no other way to reach it.

## Not changed

- The canvas element's Language submenu and the Text Box Properties Language tab keep their
  current labels.
- Text Box Properties still offers one language or "Normal".
- Collection Settings keeps "Local Language", "Language 2" and "Language 3".

## The bloom-table library

The Cell, Row, Column and Table menus are drawn by the bloom-table library
(`github:BloomBooks/bloom-table`, pinned in `src/BloomBrowserUI/package.json`). Two hooks there
let Bloom add what the library knows nothing about:

- `setHostMenuSectionProvider` asks the host, as each menu is built, for a section to draw
  directly after Content Type, given the menu's scope and the cells it acts on.
- `setHostCellSettings` lets a host setting travel with the library's own cell settings: into a
  new row or column from the one it was inserted beside, and through Paint format and
  Copy / Paste properties. A cell's languages need it because they live on the translation group
  inside the cell, not on the cell.

## Where the code is

- `src/BloomBrowserUI/bookEdit/js/tableCellLanguages.ts`: reading, applying and undoing a cell's
  languages.
- `src/BloomBrowserUI/bookEdit/js/TableCellLanguagesSection.tsx`: the menu item and its submenu.
- `src/BloomBrowserUI/bookEdit/js/tableEditing.ts`: installs both hooks; new text cells start
  with `data-default-languages="V"`.
- `src/BloomBrowserUI/bookEdit/js/translationGroupLanguages.ts` (from BL-17105-1of3-box-setup,
  which the canvas text box's Language submenu also uses): shows the chosen languages in a cell's
  translation group, adds a box for a language the cell has none for, and gives that box what page
  load gives every box.
