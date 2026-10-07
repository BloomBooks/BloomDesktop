The toolbox is the sidebar of the Edit tab. Every tool in it is a React component.

Code organization
- Files in this root folder are the generic machinery for managing the toolbox as a whole:
    - ToolboxRoot.tsx    the React root: one MUI Accordion per tool, whose body is what the
                         tool's ITool.renderPanel() returns. There is exactly one React root
                         for the whole toolbox, and each tool's panel is an ordinary child of
                         it, so React context (e.g. the MUI theme) reaches the tools normally.
    - useToolLifecycle.ts  runs one tool's lifecycle (beginRestoreSettings, showTool,
                         newPageReady, detachFromPage, hideTool) from an effect, so a tool
                         runs because it is the current tool of a showing toolbox rather
                         than because something called it. Each tool ToolboxRoot offers
                         uses it.
    - toolbox.ts         the ITool interface and the non-React orchestration: it asks the
                         server which tools this book has enabled, notices when the page or
                         book changes, and records what it finds in toolboxState.ts.
    - pageEditingMarkup.ts  the keystroke-to-markup machinery: the keypress/paste handlers on
                         the .bloom-editable divs, and the CKEditor bookmark coordination that
                         keeps the current tool's markup up to date as the user edits without
                         losing the insertion point.
    - toolboxState.ts    the toolbox's state — which tools it is offering, which tool is
                         open, which tool is running, which tools the book has enabled,
                         whether the toolbox is showing, and which page it is on — as a plain
                         external store that the React components subscribe to and toolbox.ts
                         reads and updates. (Separate module only to avoid an import cycle.)
    - registerAllToolboxTools.ts  the one list of the tools the toolbox offers. ToolboxRoot
                         shows a tool only if it has been registered, and this is where that
                         happens; both toolboxBootstrap.ts and the test harness call it, so
                         the list exists once rather than being duplicated.
    - markupSelectionPreservation.ts  preserving the user's caret while a tool rewrites the
                         markup around it.
    - toolboxZIndexes.ts  the one place defining how the Talking Book tool's "Show Playback
                         Order" disabling overlay layers against the rest of the toolbox,
                         imported by both ends so they cannot drift apart.
    - toolIds.ts         the canonical tool ids, and the single place that knows how a
                         canonical id maps to the other spellings at our boundaries (the
                         historical "Tool"/"Check" suffixes in persisted data, and the
                         English label and l10n key of a tool).
    - toolboxToolReactAdaptor.tsx  the base class real tools extend; it supplies no-op
                         lifecycle defaults so a tool implements only what it cares about.
    - toolboxBootstrap.ts  the toolbox bundle's entry point: renders ToolboxRoot, calls
                         registerAllToolboxTools(), and starts toolbox.ts.
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
   and renderPanel() (which just returns the tool's React element), plus iconPath() if its
   header should show an icon, and whichever lifecycle methods the tool needs (see the ITool
   comments in toolbox.ts). You do not wire the lifecycle methods up to anything: the toolbox
   calls them for whichever tool is running (useToolLifecycle.ts).
3. Add it to the list in registerAllToolboxTools.ts: registerOnce("myTool", () => new
   MyTool()). The id you pass there must be the one your id() returns -- registerOnce throws
   if they disagree, because the duplicate check reads the id it was given.
4. Add an XLF entry for the label, whose key follows the convention in toolIds.ts
   getToolLabelInfo() (e.g. id "music" gives key "EditTab.Toolbox.MusicTool" and English
   "Music Tool"); see .claude/skills/xlf-strings/SKILL.md.

That is all. The header (label, icon, subscription badge), the tool's checkbox under
"More...", and the alphabetical ordering are all derived from the ITool
implementation and its id, so there is no list of tools to update anywhere else.
See also the ToolboxView class comment in src/BloomExe/Edit/ToolboxView.cs.
