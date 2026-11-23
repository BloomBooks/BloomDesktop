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

- One check box per collection language: L1, then L2 and L3 when the collection has them. They
  are the collection's first, second and third languages, whatever the toolbar ticks. If two of
  them are the same language, only one entry is shown (the canvas element's Language submenu already drops duplicates
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

The ticked roles go into `data-default-languages` on the cell's `bloom-translationGroup`: `CL1`
for L1, `N1` for L2, `N2` for L3, or a list such as `CL1,N1`. They mean here what they mean in
every translation group (`ShouldNormallyShowEditable` in `TranslationGroupManager.cs`): the
collection's first, second and third languages, whether or not the toolbar ticks them.
Storing roles rather than language tags means a cell keeps its role when the book is translated in
another collection: a word-list column set to L1 shows the new collection's L1.

`CL1` is new with this feature, and so are `CL2` and `CL3`, other names for `N1` and `N2` that
anticipate retiring `N1` and `N2` once 6.6 is released (the menu reads them but does not yet
write them). Unlike `V`, the toolbar's first language, `CL1` does not change when the toolbar
does. Supporting it took these changes outside the menu:

- C# shows a box for `CL1` in any translation group (`ShouldNormallyShowEditable`,
  `Book.IsLanguageWantedInContent`), makes the box when the book's languages leave the
  collection's first language out, and sets its direction (`TranslationGroupManager`).
- A book that uses `CL1`, `CL2` or `CL3` requires Bloom 6.6 (the `collectionLanguageRoles`
  feature in `BookStorage`), since older Blooms show such a box in no language.
- Bloom Player shows a `CL1` box, like a `V` one, in the language the reader chooses
  (`updateDivVisibilityByLangCode` in its `langVisibility.ts`).

## Defaults and the toolbar

- A new text cell shows L1 only (`CL1`).
- Turning a language on or off in the toolbar does not change what a cell shows.
- Tables have not been released, so only a few test books have cells with no setting or `V`.
  Such a cell shows what any translation group with that setting shows, its menu ticks none of
  its boxes, and the first change gives it a setting the menu reads. A setting copied from such a
  cell (Paste properties, Paint format, a new row beside it) shows at once what page load will
  show for it.
- A box added or shown after the page has loaded gets the visibility and classes page load would
  give it (`languagesShownBySetting` and `languageClassesOf` in `translationGroupLanguages.ts`,
  which follow `TranslationGroupManager`). The collection's first language is marked
  `bloom-contentCL1`, as its second and third are marked `bloom-contentNational1` and `2`; among
  other things that gives such a box the rich-text editor at page load. The order boxes read down
  a group still follows the toolbar (`bloom-content1/2/3`), as for every text box.

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

- `src/BloomBrowserUI/bookEdit/js/tableCellLanguages.ts`: reading and applying a cell's languages;
  a change is recorded as a table operation, which the table library undoes.
- `src/BloomBrowserUI/bookEdit/js/TableCellLanguagesSection.tsx`: the menu item and its submenu.
- `src/BloomBrowserUI/bookEdit/js/tableEditing.ts`: installs both hooks; new text cells start
  with `data-default-languages="CL1"`, in the language `GetSettings().collectionLanguage1`.
- `src/BloomBrowserUI/bookEdit/js/translationGroupLanguages.ts` (from BL-17105-1of3-box-setup,
  which the canvas text box's Language submenu also uses): shows the chosen languages in a cell's
  translation group, adds a box for a language the cell has none for, and gives that box what page
  load gives every box, its language classes included. It also holds the front end's copy of C#'s
  rules for which languages a setting shows, which must change when those rules do.
