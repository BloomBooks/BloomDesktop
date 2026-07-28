The toolbox is the sidebar of the Edit tab. Every tool in it is a React component.

Code organization
- Files in this root folder are the generic machinery for managing the toolbox as a whole:
    - ToolboxRoot.tsx    the React root: one MUI Accordion section per tool, whose body is
                         what the tool's ITool.renderPanel() returns. There is exactly one
                         React root for the whole toolbox, and each tool's panel is an
                         ordinary child of it, so React context (e.g. the MUI theme)
                         reaches the tools normally.
    - toolbox.ts         the ITool interface and the non-React orchestration: it asks the
                         server which tools this book has enabled and drives each tool's
                         lifecycle (showTool/newPageReady/updateMarkup/...).
    - pageEditingMarkup.ts  the keystroke-to-markup machinery: the keypress/paste handlers on
                         the .bloom-editable divs, and the CKEditor bookmark coordination that
                         keeps the current tool's markup up to date as the user edits without
                         losing the insertion point.
    - toolboxState.ts    the toolbox's UI state — which tools it is offering, which one is
                         active, which ones the book has enabled, and whether the UI
                         exists yet — as a plain external store that the React components
                         subscribe to and toolbox.ts reads and updates. (Separate module
                         only to avoid an import cycle.)
    - toolIds.ts         the canonical tool ids, and the single place that knows how a
                         canonical id maps to the other spellings at our boundaries (the
                         historical "Tool"/"Check" suffixes in persisted data, and the
                         English label and l10n key of a tool).
    - toolboxToolReactAdaptor.tsx  the base class real tools extend; it supplies no-op
                         lifecycle defaults so a tool implements only what it cares about.
    - toolboxBootstrap.ts  the toolbox bundle's entry point: renders ToolboxRoot, registers
                         one instance of each tool, and starts toolbox.ts.
- Anything that is part of the implementation of a particular tool belongs in that tool's
  own child folder, one per tool.

A partial exception is a chunk of code shared by the Decodable Reader and Leveled Reader
tools, or at least not yet teased apart. Its files and folders have names starting with
Reader or containing Synphony, and for now all of it is in the readers folder.

It is a goal of our design that code outside the folder of an individual tool should not
know about the tool.

To add a new tool
1. Create a folder here whose name is the tool's canonical id (no "Tool" suffix).
2. In it, write a class that extends ToolboxToolReactAdaptor, implementing at least id()
   and renderPanel() (which just returns the tool's React element), plus iconPath() if the
   section header should show an icon, and whichever lifecycle methods the tool needs (see
   the ITool comments in toolbox.ts).
3. Register one instance of it in toolboxBootstrap.ts: ToolBox.registerTool(new MyTool()).
4. Add an XLF entry for the label, whose key follows the convention in toolIds.ts
   getToolLabelInfo() (e.g. id "music" gives key "EditTab.Toolbox.MusicTool" and English
   "Music Tool"); see .github/skills/xlf-strings/SKILL.md.

That is all. The section header (label, icon, subscription badge), the tool's checkbox in
the "More..." section, and the alphabetical ordering are all derived from the ITool
implementation and its id, so there is no list of tools to update anywhere else.
See also the ToolboxView class comment in src/BloomExe/Edit/ToolboxView.cs.
