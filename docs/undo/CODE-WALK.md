# The page history API against Bloom's code

On 2026-10-15, five read-only passes through the code tested the design in APPROACHES.md (recorded
DOM mutations, with the API of section 11.8) against real places in the Edit tab. They favoured the
hard cases: requests to C# that change state, files, and work done after an `await`. None of them
read how undo works today. Line numbers are from the `more-undo-exploration` worktree. A claim that
was inferred rather than read says so.

The first part ranks the issues that cut across the places walked. The second part goes place by
place. The third is the inventory of requests to C#.

Paths: front-end paths are under `src/BloomBrowserUI/`, C# paths under `src/BloomExe/`.

---

## Part 1. Issues that cut across the code, most threatening first

### 1. Page changes after an `await`, a reply or an event are the normal case

Rule 2 opens an "Undo edit" transaction for a real click or key press. In much of the code the click
changes nothing itself; the page changes in a reply handler, a websocket message, a player event, a
React effect or a load listener. The click's transaction is empty and dropped, and rule 1 joins the
later changes to the user's previous action.

- Talking Book: the id minted after awaits (`bookEdit/toolbox/talkingBook/audioRecording.ts:1474`),
  markup and `recordingmd5` after `audio/endRecord` (`:1510-1537`), `data-duration` from the player's
  `durationchange` and an `onerror` retry loop that waits for the file (`:321-337`, `:1542-1550`),
  Clear's reply handlers (`:2291-2302`), Import's id and markup.
- Adjust Timings: the page is split into spans by a React effect in the root window
  (`bookEdit/toolbox/talkingBook/AdjustTimingsDialog.tsx:178-230`).
- Video: results from the C# reply and a websocket message
  (`bookEdit/toolbox/signLanguage/signLanguageTool.tsx:815-818`,
  `bookEdit/ProcessVideoProgressDialog.tsx:59-72`).
- Pictures: the image gallery after an await (`react_components/image-gallery/ImageGalleryDialog.tsx:143-154`),
  `editView/pasteImage`, where C# calls `changeImage` in the page (`Edit/EditingModel.cs:2158`), the AI
  image editor's replacements, and `editView/addImageFromUrl`, whose result arrives over a websocket
  (`bookEdit/js/linkGrid.ts:94-105`).
- Text: `editView/paste` and the top bar's cut, copy and paste make C# run script in the page
  (`web/controllers/EditingViewApi.cs:448-471`).
- Music "Choose…": the attribute is set in a GET's callback (`bookEdit/toolbox/music/musicToolControls.tsx:418-432`).

Grouping these with the previous action is still safe (undo returns the page to a state that it was
in), but the undo steps are wrong from the first step of the move, and APPROACHES §10 assumes that
the first step needs no wrapping. These actions need `beginTransaction` at the click and `end()` when
the reply has been applied, from the start.

A second, worse form: a picture's `load` reaction can land after the user's next input (a large
picture, a slow disk). The refit then joins whatever the user did next, and undoing that takes out
the refit and leaves the picture fitted to the old one's shape
(`bookEdit/js/canvasElementManager/CanvasElementBackgroundImageManager.ts:635-674`,
`CanvasElementManager.ts:1883-1925`). At page load the same thing can put back the half-converted
background picture that Bloom already treats as broken (BL-16870, `CanvasElementBackgroundImageManager.ts:180-207`).
That state did exist for a moment, so it meets the letter of the safety rule, but it is a broken page.
Rule 1's premise, that Bloom's reactions finish before the next input, does not hold for anything
that waits on a load. Proposed fix: C# returns each new picture's pixel size (it already measures it,
`web/controllers/AiImageEditorApi.cs:2059-2062`), so that the refit runs synchronously inside the
change's transaction.

### 2. Bloom writes markup that the save keeps without any click or key press

- Pointer entering a picture: `SetImageTooltip` awaits C# and then writes `data-title` and `title`
  (`bookEdit/js/bloomImages.ts:944-951`), which saved pages carry
  (`src/BloomTests/Book/BookStorageTests.cs:3037`, `:3150`).
- Selection: `UpdateImageTooltipVisibility` rewrites `title` on every bloom-canvas at each selection
  change (`bloomImages.ts:879-883`, `:935`), so a plain selection click is a non-empty "Undo edit" step.
- `mouseenter` buttons without `bloom-ui`: the widget chooser button (`bookEdit/js/bloomWidgets.ts:28-39`),
  `moveButton` (`bloomImages.ts:186-210`), `ui-mouseOver` (`:1359-1366`), `deleteButton`
  (`bookEdit/js/bloomEditing.ts:180-198`).
- Video selection writes `bloom-selected`, which is saved and read back on load
  (`bookEdit/js/videoUtils.ts:65-89`, `signLanguageTool.tsx:1145-1153`).
- Zoom writes the style of `#page-scaling-container` in the page document
  (`bookEdit/workspaceRoot.ts:392-405`).

Each write outside an input joins the latest transaction and clears redo, so moving the mouse across
a picture or a widget after an undo loses Redo, and the user cannot see why. Safe, but everywhere.
The filter of rule 6 was derived from the save path; it needs an audit of `mouseenter`, `mouseleave`
and selection handlers too. Derived values such as `title` and `data-title` can go in the filter or
in `doOutsideHistory`; hover buttons should carry `bloom-ui`.

### 3. Page load has no defined end

Tool setup runs after async chains and 100 ms retries, and the toolbox calls every tool's
`newPageReady` again 600 ms later (`bookEdit/toolbox/toolbox.ts:561-573`;
`bookEdit/toolbox/games/GameTool.tsx:1780-1822`, `:1791`, `:1811`). The placeholder text arrives after
an await (`bookEdit/js/PlaceholderProvider.ts:49-74`). The background picture conversion waits for a
load. A write that lands after the user's first input joins it, including the Games tool's random
shuffle of targets (`GameTool.tsx:2107-2155`): undoing the first edit un-shuffles them.

Opening a tool from the toolbox is a real click, so the tool's setup writes (Motion's
`bloom-hideImageButtons` and default rectangles, `bookEdit/toolbox/motion/motionTool.tsx:106`,
`:256-269`; Games) become an "Undo edit" step, and undoing it leaves the tool out of step with the
page.

The design needs a defined moment when the page is ready, after which the history accepts
transactions (load-time page content delays cleared), and a rule for tool setup: outside the history,
or a boundary.

### 4. Files: deletions and overwrites that the rules don't cover, and a rule that C# can't apply

Deletions of files that the page names, during editing:

- `signLanguage/deleteVideo` moves the video that the page names to the Recycle Bin after a WinForms
  confirm (`web/controllers/SignLanguageApi.cs:690-713`). It is on no list. Undo can bring back a
  `<video>` whose file is gone. As a boundary it would clear the history even when the user cancels
  the confirm, which runs inside the request.
- `DeleteSupersededAiImageFiles` deletes `ai-image*` files that C#'s saved DOM no longer references
  (`AiImageEditorApi.cs:1863`, `:2442-2467`). That DOM knows nothing about the page's history. Today
  the launch reloads the page first; once reloads go (PR 8209) it can delete a file that the open
  page's history names. APPROACHES §3.4's claim that it removes nothing that an undo needs rests on
  that reload.

Overwrites of files that pages name:

- `editView/addImageFromUrl` writes `bookButton-<bookId>.png` with `RobustFile.Create`
  (`Edit/EditingView.cs:1744-1754`).
- `addPage` overwrites smart-page script files with the template's copy (`Book/Book.cs:3921-3926`).
- The Games tool's built-in sounds: `fileIO/copyFile` always overwrites (`web/controllers/FileIOApi.cs:303`),
  usually with the same bytes (`GameTool.tsx:776-794`).
- Image metadata is written into the picture file in place: Set Image Information
  (`EditingView.cs:631-650`, `ImageProcessing/ImageUtils.cs:2832-2866`), "Add this info to all images"
  over every `.png`/`.jpg` in the folder, including files that only the history names
  (`Book/ImageUpdater.cs:131-143`), and the paste of a slot's own file onto the same slot
  (`Edit/PageEditingModel.cs:49-52`, `:88-101`).
- Whole-folder passes rewrite pictures in place: `ProcessBook` (`Book/BookProcessor.cs:157-167` →
  `ImageUtils.cs:1479-1576`) and the media level 1 migration (`Book.cs:1170`).

The proposed C# refusal to overwrite a media file cannot be keyed on "the request is on
`kRequestsThatEndTheHistory`": many writes run in `SaveThen` callbacks after their request has ended
(`web/controllers/CopyrightAndLicenseApi.cs:201-253`, `EditingModel.cs:1937-1939`), and the
whole-folder passes have no request at all. C# also does not know what the live page names. And some
overwrites are legitimate: the video's ffmpeg fallback copy and in-place re-encode of a file that the
same request just created (`SignLanguageApi.cs:197`, `:427`), the Aeneas output and "Edit timings
file" (`web/controllers/AudioSegmentationApi.cs:853-857`, `:884`; `AdjustTimingsDialog.tsx:527`).
C# needs a rule that it can apply: media files only, not a file that this request created, and a
flag that the front end keeps up to date for "a page history exists".

The rule "no action deletes a file while editing" is also worded too broadly: deleting a file that no
page ever named is harmless (`AiImageEditorApi.cs:2373`, `ImageUtils.cs:862-866`). It should read "no
action deletes a file that any page state may name".

File names can be reused within a session: `GetUnusedFilename` takes the first counter that is not on
disk (`ImageUtils.cs:1214-1286`), so a name freed by a deletion can be given to a different picture.
`RuntimeImageProcessor` caches processed pictures by path, checked against the write time
(`RuntimeImageProcessor.cs:160-174`), and keeps a "serve unprocessed" list by path with no check at
all (`:133-134`). Copies that keep their source's write time (inferred) could then make undo show the
wrong picture. "A name that no file has yet" should be "a name that no file has had in this session".
The WebView itself does not cache book files (`RequestInfo.cs:232-235`).

### 5. `kRequestsThatEndTheHistory` is incomplete, and an interceptor cannot see everything

Missing requests that change book state: `editView/topBar/contentLanguageUsageChange`,
`editView/setTopic`, `editView/setCustomPageLayout` (each changes the book in C# before `SaveThen`:
`EditingModel.cs:946`, `:1898`; `EditingViewApi.cs:265`), `signLanguage/deleteVideo`,
`editView/saveToolboxSetting` when the leveled reader level changes (`EditingModel.cs:1814-1835`), and
`editView/jumpToPage` after a custom layout conversion (`bookEdit/toolbox/canvas/customXmatterPage.tsx:549-574`).

Blind spots:
- `fileIO/copyFile` serves Import, canvas-element duplication and game sounds; the URL alone cannot
  tell Import apart.
- GETs with side effects: `editView/chooseWidget`, `music/ui/chooseFile`, the image-metadata GET that
  stores the file name for the POST (`EditingView.cs:608`).
- Reloads that C# starts never pass the interceptor: Report a Problem (`WorkspaceView.cs:1912-1940`), a
  UI language change (`EditingModel.cs:243-262`), videos edited by another program
  (`SignLanguageApi.cs:1043-1085`).
- Requests that reload do not end the history before C# acts: `pageList/contextMenuItemClicked` runs
  its command 100 ms later on a Task (`web/PageListApi.cs:146-174`), and `editView/duplicatePageMany`
  replies before it acts.
- A page with a custom layout under a legacy theme sends `setCustomPageLayout` during page load
  (`customXmatterPage.tsx:490-503`).
- axios runs request interceptors asynchronously, so synchronous page changes made right after a post
  are recorded and then wiped by the clear (Clear audio, `audioRecording.ts:2310-2317`). Consistent,
  but surprising.

### 6. Saving, and what C# does with the live page

- Every save writes to the live page: `recordFractionOfPageOnImageSlots`
  (`bloomEditing.ts:1470-1474`, `imageTargetResolution.ts:325-361`), besides the destructive strip
  (`bloomEditing.ts:1458-1463`). Once saves stop reloading, that write joins the latest transaction.
  Write it to the clone that is saved, or in `doOutsideHistory`.
- The save keeps only some of the page div's attributes (`Book/HtmlDom.cs:2252-2320`: class, lang,
  page colours in `style`, eight `data-` attributes). Undoing any other page-div attribute does
  nothing to the book, and C#'s own edits to such attributes (`EditingViewApi.cs:265` removes
  `data-tool-id`) never reach the live page. The design assumes the page DOM is what is saved; that
  holds for content, not for the page div.
- A full save can rename the book folder when the title changes (`Book.cs:4427-4428`, `:5078-5081`;
  `BookStorage.cs:1917-1999`). Today the reload hides it. Once a save leaves the page loaded, its
  `<base>` points at a folder that is gone (`BookStorage.cs:2391-2405`). Pictures survive through a
  server fallback (`BloomServer.cs:1420-1438`); video, background audio and widgets would not
  (inferred). This affects PR 8209 more than the history.
- Whole-book changes need the current page saved first. Book settings, content languages and the
  book's copyright rewrite C#'s stale copy of the current page and save the book
  (`Book.cs:3098-3104`, `:5075`). That conflicts with the proposed rule that the functions of a change
  outside the page don't read the page, and undo makes changes outside the page before it restores
  the page, so a save inside `bidirectional(before)` would capture the page as it was before the undo.
  The plan in §11.5 for making these undoable later must say who saves, and when.
- `SaveStateForFullSaveDecision` is taken from C#'s copy, so retaking it after each save works as
  §11.7 says; `_featureRequirementsBeforeEdits` must be retaken with it (`EditingModel.cs:1515-1525`).

### 7. Playback writes the page

`fixHighlighting` (at Play and Listen) replaces text nodes with `span.ui-enableHighlight` in
sentences that have three or more spaces, and `revertFixHighlighting` assigns `innerHTML` back when
playback ends (`audioRecording.ts:5088-5273`). So Play is an undo step; the revert, minutes later,
joins an unrelated one; the revert makes new text nodes, so any older transaction that holds the old
ones fails the check and the history is dropped; typing during a pause is erased (a bug today); and
the Music tool's preview leaves the spans in the page (`musicToolControls.tsx:307-309`,
`audioRecording.ts:2079-2084`). The fix is to use CSS highlight ranges, as the rest of the
highlighting already does (`audioRecording.ts:1078-1112`).

### 8. Components and the restore

- React does not re-read synchronously: `setState` in `onAfterUndoOrRedo` only schedules a render,
  and `useEffect` writes (`GameTool.tsx:900-922`) run in a later task, outside any input. Controls that
  write the inverse of a cached value (`GameTool.tsx:1118`, `:1152`, `:1164`) stay wrong until React
  commits. Controls should read the page when acted on.
- `ResizeObserver` has no `takeRecords()` and fires in a later rendering step
  (`motionTool.tsx:562`, `CanvasElementEditingSuspension.ts:73`). Each one needs its own fix.
- Talking Book's visibility observer reruns all of `handleNewPageReady`, with markup writes after
  awaits, on a class change of `<html>` or `.bloom-wordChoice` (`audioRecording.ts:3066-3101`,
  `:2877-2933`). An undo that touches those classes starts a markup pass that clears redo.
- Undo puts back the same node objects, so a change detector that compares nodes never notices an
  undo: the Canvas tool panel's 250 ms poll (`bookEdit/toolbox/canvas/CanvasToolControls.tsx:305-319`).
- The toolbox outlives the page and keeps its own record of which files exist (`haveAudio`,
  `audioRecording.ts:4272-4317`; video stats, `signLanguageTool.tsx:1218-1237`). Refreshing it needs a
  C# round trip, but `onAfterUndoOrRedo` must be synchronous. Until it refreshes, stale values steer
  Import's "replace?" warning, the mode switch and Clear (`audioRecording.ts:4990-5007`, `:2604-2611`,
  `:2272`).
- Records that change nothing: by the DOM specification, `classList.add` of a token already present
  still queues a record (not tested). The quiz and placeholder observers emit these after every undo
  (`bookEdit/js/simpleComprehensionQuiz.ts:76-87`, `PlaceholderProvider.ts:109-129`). Rule 1 must test
  for a net change before it clears redo.

### 9. The Games tool's Play tab needs the history off, not just boundaries

While playing, every drag is real input that changes the page, and bloom-player keeps maps keyed by
element and its own animation state (bloom-player `dragActivityRuntime.ts:36`, `:45-56`,
`:1344-1377`). An undo during Play would not match those maps. The API has no way to suspend
recording from entering Play to leaving it, with Undo disabled. Also unstated: if a click handler
calls `clearUndoAndRedo()` and then changes the page, does the rest of that event open a new "Undo
edit" step?

### 10. Audio ids are shared, and `data-duration` is not recomputed

- The planned fix for recording (a new id per recording, so C# writes a new file) assumes one element
  per id. Data-book fields share audio ids between the cover title and the title page
  (`Book/HtmlDom.cs:3966-3971`); the front end copies the data div's id, duration and checksum onto an
  editable (`bookEdit/toolbox/canvas/canvasControlTextMenuItems.ts:152-197`); custom layouts duplicate
  ids within a page (`Book/BookData.cs:87-88`); code looks up `#id`, which finds only the first
  (`audioRecording.ts:5263`, `:2297-2299`, `:1422`). In text-box mode the id also names
  `audio/<id>_timings.txt` (`AdjustTimingsDialog.tsx:461`). Whether C#'s data-book sync carries an
  editable's own id to other pages is not checked.
- `data-duration` is written only for the current element when the player loads it
  (`audioRecording.ts:1542-1550`), sometimes stale (`:2294-2296`). It is not recomputed from other
  content, so `doOutsideHistory` does not fit rule 3: after undoing a re-recording the old id would
  carry the new duration. Record it in the recording's transaction, or have C# compute it from the
  files at save (C# already strips durations whose file is gone, `Book.cs:5141-5160`).
- A too-short Speak press clears the whole history, though C# then restores the files
  (`Edit/AudioRecording.cs:524-538`, `:723`). Until the new-id fix lands, every mis-tap erases undo.

### 11. Transactions left open

Bloom's drags use mouse events and end on `buttons === 0` at the next move
(`CanvasElementPointerInteractions.ts:573-576`, `:622-625`;
`CanvasElementHandleDragInteractions.ts:348-351`); mouse events have no cancel event, so "end it on
pointercancel" does not match the code. The Motion tool's jQuery UI drags have only `stop`. The colour
picker can close through `hideColorPickerDialog`, which skips both OK and Cancel
(`react_components/color-picking/colorPickerDialog.tsx:465-467`), so a transaction opened when it
opens would stay open.

### 12. Which frame's clicks count

The toolbox frame and the root window change the page: the reader tool switch sets a body class
(`bookEdit/toolbox/readers/ReaderToolSwitch.tsx:58-61`), the video tool removes the `<video>`, the
Book and Page Settings dialog applies page settings live and restores a snapshot on Cancel
(`bookEdit/bookAndPageSettings/BookAndPageSettingsDialog.tsx:328-353`), and the image gallery, the
colour picker and the AI image editor live in the root window. Rule 2 does not say whether a click in
the toolbox, the top bar or a root-window dialog opens an "Undo edit" transaction.

### 13. Scope and product questions that the code raises

- Add, Duplicate, Delete, Move and Paste Page all navigate to another page
  (`EditingModel.cs:606-785`, `Edit/PageThumbnailList.cs:278-306`), so they are never undoable under
  a history per page; Delete Page in particular. If one ever stops navigating, C# renumbers the
  current page and changes its `side-left`/`side-right` on its copy only, and the next save writes the
  stale class back from the live page (`HtmlDom.cs:2281`).
- A picture shared by several pages (duplicates, game target copies): today one metadata edit changes
  the credits everywhere; with a new file per edit only this page changes, and the credits page
  (`web/controllers/ImageApi.cs:119-194`) lists the picture twice.
- An AI image editor commit changes and saves other pages too (`AiImageEditorApi.cs:1834-1856`), so
  only part of it can be undone.
- Launching the AI image editor on a book that needs its page layout update empties the editor and
  goes back to the page (`EditingModel.cs:1071-1101`): a boundary that §3.3 does not list.

### 14. Questions that the walk answered

- **The leveled reader level stays outside the history.** The body's `data-leveledreaderlevel` is
  read only by `ABC-Reader-XMatter.less:383-396`, for display, and body attributes never reach the
  book from the page (`Book.cs:4312-4318`). Today every level change reloads the page
  (`EditingModel.cs:1814-1835`); instead C# can return the value and the page can write it in
  `doOutsideHistory`.
- **Adjust Timings need not be a boundary.** `_timings.txt` is read back only when the user picks
  "Apply timings file" (`audioRecording.ts:4964`) or inside the request that wrote it
  (`AudioSegmentationApi.cs:862`); the page does not name it.
- **`toolbox/decodable` and `toolbox/leveled` fit `addChangeOutsidePage` with `bidirectional`.** They
  set a body class in C#'s DOM while the front end sets the same class on the page body
  (`ReaderToolSwitch.tsx:58-69`, `web/controllers/ToolboxApi.cs:30-47`).
- Cropping writes only inline styles (`CanvasElementHandleDragInteractions.ts:413`, `:1015-1079`);
  C# crops only copies made for publishing. Video trimming writes only `#t=` on the source's `src`
  (`signLanguageTool.tsx:417-436`). Video recording and import write files under new GUID names
  (`SignLanguageApi.cs:96-129`, `:565-643`).
- §3.3 assumes that C# can return what it changed. For Set Image Information it cannot today: its
  work runs in a `SaveThen` callback after the reply has gone (`EditingModel.cs:2252-2254`). The file
  write has to move out of `SaveThen`.

---

## Part 2. Place by place

Each entry: what the user does, what the code does today, how it would change, what it showed.

### Talking Book: recording, re-recording, Import, Clear, mode switch

- Today: Speak calls `startRecordCurrentAsync` (`audioRecording.ts:1319-1389`), which removes the
  split markup (`:4809-4819`), may await a highlight repair, mints an id if the element has none
  (`:1466-1478`), and posts `audio/startRecord`. C# moves the old `.wav`/`.mp3` to `.bak`
  (`AudioRecording.cs:540-665`). Mouse-up posts `audio/endRecord`; C# writes the files later
  (`:465-522`). The page markup and checksums change after the reply (`audioRecording.ts:1510-1537`).
  Import copies over `audio/<id>.mp3` through `fileIO/copyFile` (`:4999-5061`, `FileIOApi.cs:282-303`).
  Clear posts `audio/deleteSegment` per id; C# deletes both files (`AudioRecording.cs:914-937`). "Has a
  recording" always asks C# whether the file exists (`AudioRecording.cs:183-232`, `:863-877`). The
  mode switch posts a collection setting and rewrites the box into spans or only sets
  `data-audiorecordingmode` (`audioRecording.ts:2584-2658`).
- Change: `beginTransaction(kRecordL10nKey)` synchronously in `startRecordCurrentAsync` before its
  first await (the code must not gain an await before `recording = true`, `:1337-1340`); `end()` after
  `finishNewRecordingOrImportAsync`, `cancel()` on a press that is too short. Import: the same around
  the copy. Clear, once it stops deleting: a `doTransaction` that gives the sentence a new id. Mode
  switch: `doTransaction`; `talkingBook/defaultAudioRecordingMode` stays off the boundary list.
- Showed: issues 1, 8, 10 above.

### Adjust Timings and forced alignment

- Today: opens in the root window; a React effect splits the box into spans and mints ids into the
  toolbox's `sentenceToIdListMap` (`AdjustTimingsDialog.tsx:178-230`, `audioRecording.ts:4519-4557`).
  Aeneas writes `audio/<id>_timings.txt` (`AudioSegmentationApi.cs:519-527`, `:853-857`). OK writes the
  end times and `bloom-postAudioSplit` (`AdjustTimingsDialog.tsx:389-402`); Cancel leaves the spans
  (`:237-242`).
- Change: `beginTransaction(kAdjustTimingsL10nKey)` in `showAdjustTimingsDialog` before its first
  await; `end()` on OK, `cancel()` on Cancel, which also takes out the spans. Not a boundary.
- Showed: page changes from React effects in another frame; the toolbox's id map is not rolled back.

### Sign language video

- Today: recording posts the blob; C# runs ffmpeg into a new GUID file and the page takes it in the
  reply. Import processes on a background thread and reports over a websocket. Trim writes only
  `#t=`. Delete recycles the file the page names (`SignLanguageApi.cs:690-713`).
- Change: `beginTransaction` at Stop and at Import, `end()` when the result is applied; Delete as a
  `doTransaction` that removes the `<video>` while C# keeps the file. Block input while ffmpeg runs,
  as Import's modal dialog already does.
- Showed: issues 1, 4 above; `bloom-selected` saved; external edits detected for video only.

### Playback and music

- Today: `fixHighlighting` and its revert write the page (issue 7). Music writes
  `data-backgroundaudio` and `data-backgroundaudiovolume` on `.bloom-page`; "Choose…" is a GET that
  copies under a unique name (`web/controllers/MusicApi.cs:148-154`).
- Change: CSS highlight ranges; `beginTransaction` for "Choose…", ended in its callback.

### Set Image Information, and "Add this info to all images"

- Today: the dialog's GET stores the file name in C# (`EditingView.cs:568-628`); OK posts without
  waiting; C# writes the metadata into the existing file in a `SaveThen` callback, updates its copy of
  the page and reloads (`CopyrightAndLicenseApi.cs:201-253`, `EditingModel.cs:1419-1446`). "All
  images" rewrites every picture in the folder and saves the book (`ImageUpdater.cs:20-143`).
- Change: C# copies the file to a new name, writes the metadata into the copy, and replies with the
  new `src` and credits, outside `SaveThen`; the page applies them in a `doTransaction`, keeping the
  `src`'s query string (`bloomImages.ts:558-577`). "All images" stays a boundary.
- Showed: issues 4 and 13 above.

### The AI image editor

- Today: launching saves and reloads (`AiImageEditorApi.cs:340-409`). Commit imports under new
  `ai-image*` names, may convert PNG to JPEG and delete the PNG it just wrote, changes slots on other
  pages and saves, then deletes superseded files (`:1750-1878`, `:2137-2467`). Current-page slots are
  applied by `applyAiImageEditorReplacements` (`bookEdit/aiImageEditor/aiImageEditorPageCommands.ts:138-202`).
- Change: wrap `applyAiImageEditorReplacements` in a `doTransaction`; C# stops deleting during the
  commit and leaves the files to the cleanup, which must then also handle `.jpeg` and `.webp`
  (`AiImageEditorApi.cs:2432-2436`).

### The background picture of a canvas page

- Today: at page load an old-style picture is converted, half synchronously and half after the
  picture loads (`CanvasElementBackgroundImageManager.ts:209-305`, `:635-674`); a change clears the
  crop and refits after the load (`CanvasElementManager.ts:1947-1963`); the metadata button and alt
  text are derived from attributes and written from async localization callbacks
  (`bloomImages.ts:1231-1355`).
- Change: C# returns the picture's size so the refit is synchronous; `onAfterUndoOrRedo` refreshes
  the metadata button and alt text.
- Showed: issues 1 and 3 above; the alt text is already stale after any picture change.

### Canvas elements: select, drag, resize, rotate, Alt+drag duplicate, colour

- Today: `mousedown` selects and writes `title` on every canvas; dragging writes position on each
  animation frame (`CanvasElementPointerInteractions.ts:648-700`); handle drags add document listeners
  (`CanvasElementHandleDragInteractions.ts:325-791`); Alt+drag duplicates inside `mousedown`
  (`CanvasElementDuplication.ts`), starting an audio copy that is not awaited (`:387`). The colour
  picker lives in the root window and calls `onChange` on every drag step; Cancel writes the swatch
  colour back (`colorPickerDialog.tsx:306-335`).
- Change: `beginTransaction` at `mousedown`, before the duplicate, so both are one step; `end()` where
  the code already ends the drag, including the `buttons === 0` paths. The colour picker:
  `beginTransaction` when it opens, `end()` on OK, `cancel()` on Cancel, which also restores a
  default colour that today's Cancel turns into an explicit one (inferred). The tool panel re-reads in
  `onAfterUndoOrRedo` instead of comparing nodes.
- Showed: issues 2, 8 and 11 above.

### The Games tool

- Today: tabs keep their state on `window.top` and classes outside `.bloom-page`; Play suspends
  editing and lets bloom-player move elements; toggles write page attributes from cached React values;
  built-in sounds overwrite; "Choose…" copies under a unique name and writes after an await; a
  MutationObserver copies each draggable to its target (`GameTool.tsx:930-971`); setup shuffles at
  random on page load.
- Change: toggles read the page when clicked, inside a `doTransaction`; "Choose…" in a
  `doTransaction` around the await; React state re-read in `onAfterUndoOrRedo`; Play with the history
  suspended.
- Showed: issues 3, 8 and 9 above.

### The Motion tool

- Today: opening the tool hides the image buttons and may write default rectangles; jQuery UI drags
  write `data-initialrect`/`data-finalrect` on `stop`; a MutationObserver on the picture's `src` and a
  ResizeObserver on the canvas can write; Preview creates its own `AudioRecording` and a timer, whose
  cleanup removes the page's `Landscape` class (`motionTool.tsx:874`, probably a leftover bug).
- Change: `beginTransaction` in jQuery UI's `start`, `end()` in `stop`; `onAfterUndoOrRedo` drains
  the observer and redraws; `onBeforeUndoOrRedo` stops Preview.

### Quiz, placeholders, widgets

- Today: the quiz's observer rewrites class `empty` on every text change and copies `checked` from
  the class at load (`simpleComprehensionQuiz.ts:44-87`); placeholders write `bloom-blank`; the widget
  chooser unzips into a new folder (`Book/WidgetHelper.cs:33-56`).
- Change: the derived classes in `doOutsideHistory`; the quiz re-reads `checked` in
  `onAfterUndoOrRedo`; hover buttons get `bloom-ui`; the widget chooser in a `doTransaction`.
- Showed: a restored iframe reloads its document, so the same element is not the same state for
  iframes (harmless for editing).

### Saving

- Today: no timer; C# saves through `SaveThen` (`EditingModel.cs:1940-1959`) on page selection, tab
  change, collection close, UI language change, Report a Problem and the actions that save. The save
  writes to the live page and strips it, C# keeps only some page-div attributes, a full save pushes
  the data-div into every page and can rename the folder, and then the page reloads.
- Change: measure on the live page, write to the clone; rename the folder only when the page is going
  away, or update `<base>` in `doOutsideHistory`.
- Showed: issue 6 above.

### Page list actions and content languages

- Page list actions all navigate (issue 13). Content languages from the top bar change every page in
  C# inside the request handler and are missing from the list (issue 5); in a Team Collection book
  `PrepareForEditing` can run the full bring-up-to-date, including `CleanupUnusedSupportFiles`, with a
  page open (`Book.cs:4854-4858`, `:1172`).

---

## Part 3. Requests from the Edit tab that can change state

Classes: **a** no undo concern (nothing on the page depends on it); **b** must be on
`kRequestsThatEndTheHistory`; **c** a candidate for `addChangeOutsidePage`; **d** writes a file that a
page names (new name or overwrite); **e** unclear; **reload** saves and navigates, so the page load
ends the history.

| Request | What it changes | Class | On the list today |
|---|---|---|---|
| editView/pageContent | saves the page; strips the live DOM; reload | a | no |
| pageList/pageMoved, pageList/contextMenuItemClicked, edit/pageControls/addPage, duplicatePage, deletePage, editView/duplicatePageMany, addPage | book HTML; save and navigate (addPage also overwrites script files) | reload (addPage: d) | no |
| changeLayout | this page or every similar page | b | yes |
| editView/jumpToPage | save and navigate | reload | no |
| common/saveChangesAndRethinkPageEvent | save and reload | b | yes |
| aiImageEditor/saveThenLaunch | save and reload; sometimes a page layout update over the whole book | reload | no |
| editView/topBar/layoutChoiceChange | page size and orientation | b | yes |
| editView/topBar/contentLanguageUsageChange | content languages, set before the save | b | **no** |
| book/settings | appearance.json, meta.json, content languages | b | yes |
| book/settings/deleteCustomBookStyles | deletes customBookStyles.css | b | yes |
| editView/setTopic | topic in the data-div, set before the save | b | **no** |
| editView/setCustomPageLayout | page class, data-div, QR code; also sent during page load | b | **no** |
| copyrightAndLicense/bookCopyrightAndLicense | book copyright and license | b | yes |
| copyrightAndLicense/imageCopyrightAndLicense (POST) | metadata written into the picture file, or every picture | b, d | yes |
| copyrightAndLicense/imageCopyrightAndLicense (GET) | stores the file name for the POST | a (side effect) | no |
| copyrightAndLicense/unlockOriginalCopyrightNotice | data-div; save and reload | b | yes |
| toolbox/decodable, toolbox/leveled | a body class in C#'s DOM | c, `bidirectional(bool)` | no |
| editView/saveToolboxSetting | tool state in meta.json; the leveled reader form can reload | a, e | no |
| aiImageEditor/commit | new files; other pages saved; superseded files deleted | e | no |
| editView/pasteImage | a new file, or metadata into the slot's own file | d | no |
| imageGallery/imageGalleryResult | a new file | d | no |
| editView/addImageFromUrl | overwrites `bookButton-<bookId>.<ext>` | **d, overwrite** | no |
| editView/chooseWidget (GET) | a new `activities/` folder | d | no |
| audio/startRecord, audio/deleteSegment | records over, or deletes, `audio/<id>` | b | yes |
| audio/endRecord | finishes the recording | b (covered by startRecord) | no |
| audio/copyAudioFile | copies to a new id | d | no |
| fileIO/copyFile | Import overwrites; duplication writes a new id; game sounds overwrite | b / d / d | Import only |
| fileIO/chooseFile with a destination | a new file | a / d | no |
| fileIO/writeFile | overwrites `_timings.txt`, which no page names | a | no |
| audioSegmentation/getForcedAlignmentTimings | writes `_timings.txt`; returns end times | a | no |
| signLanguage/recordedVideo, chooseVideo, processVideo | new GUID files | d | no |
| **signLanguage/deleteVideo** | recycles the video that the page names | **b** | **no** |
| music/ui/chooseFile (GET) | a new file | d | no |
| readers/io/readerToolSettings, readers/io/allowedWordsList (DELETE) | collection settings and word lists that the reader markup is computed from | e | no |

No undo concern (class a): zoom (but see issue 2), the source text tab, reader default level and
stage, user settings, service keys, the default recording mode, custom palette colours, modal state,
text selection, accessibility checks, recording devices, the clipboard, logging and analytics, help,
showing files, and the requests that only make C# run script in the page (`editView/paste`,
`editView/topBarButtonClick`; see issue 1).
