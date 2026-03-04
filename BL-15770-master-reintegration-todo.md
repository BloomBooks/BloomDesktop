# BL-15770 Master Reintegration Todo

Goal: land `BL-15770RefactorCanvas` as a PR that merges cleanly with `master`, with all relevant upstream behavior preserved.

Background: we have done a massive refactoring and meanwhile master has had many changes to files that we have touched. A simple merge is not going to end well. Therefore we looked at all of the changes to related files that have happened since we branched off of master.

Status key:
- `[ ]` not done
- `[~]` in progress
- `[x]` done

## Recommended Execution Order (for smooth PR)

### Phase 1 — Low-conflict merges first (unblocks later phases)
Most files in this phase were NOT changed on the branch, so upstream commits should apply cleanly. Backend overlap items (13-15) are intentionally grouped here for early conflict resolution.

1. **Paste infrastructure in `toolbox.ts` + `toolboxBootstrap.ts`** (items 17, 18) — prerequisite for item 11.
2. **`toolboxToolReactAdaptor.tsx` `isXmatter` signature** (item 19) — prerequisite for item 6.
3. **`editMode.less` CSS additions** (item 21) — prerequisite for items 2-5.
4. **Backend/host-frame overlap merges**: items 13, 14, 15.

### Phase 2 — Backend API additions + new files (low-conflict)
5. **Custom cover API endpoints** (item 1) — `EditingViewApi.cs` has branch changes, but in different functions.
6. **Bring over new files** `customPageLayoutMenu.tsx` + `customXmatterPage.tsx` (item 20) — these don't exist on branch, just copy from master.

### Phase 3 — Manual re-implementations (the hard ones, in dependency order)
7. **Origami canvas structure** (item 7) — prerequisite for item 8.
8. **Background image manager robustness** (item 8).
9. **Language submenu + Field Type submenu** (items 3, 4) — into registry/context controls.
10. **Image field type / become-background** (item 5) — depends on items 3-4 patterns.
11. **Frontend custom layout menu wiring** (item 2) — depends on items 1, 20.
12. **Canvas tool gating for custom page** (item 6) — depends on item 19.
13. **Text color targeting for `data-derived` fields** (item 10).
14. **Drag/drop coordinate consistency** (item 9) — verification pass.

### Phase 4 — Paste pipeline (depends on Phase 1 toolbox.ts merge)
15. **Paste side-effects in `bloomEditing.ts`** (item 11) — the `scheduleMarkupUpdateAfterPaste` calls + `wrapWithRequestPageContentDelay` wrapping + Ctrl+V robustness. Port the **final master state**, not each incremental commit.

### Phase 5 — Source bubble / cleanup + lint
16. **Source bubble recompute and qtip cleanup** (item 12).
17. **`bloomVideo.ts` trivial comment update** (item 22).
18. **Type-safety lint cleanups** in `bloomEditing.ts` (item 23) — `== null` → `=== null`, unused-param prefixes, `catch (e)` → `catch`, remove `String` interface augment.

### Phase 6 — Validation
19. Run focused tests and re-check merge-base diff for missed upstream behavior.

## Work Tracker

| ID | Priority | Merge Mode | Status | Area | Upstream commits | Files to update (this branch) | Notes / Acceptance |
|---|---|---|---|---|---|---|---|
| 1 | P0 | Normal merge or cherry-pick + resolve | [ ] | Custom cover/layout API endpoints | `25d0286ca` | `src/BloomExe/web/controllers/EditingViewApi.cs` | Add/verify `editView/toggleCustomPageLayout` and `editView/getDataBookValue`; preserve save-then-rethink flow and empty-custom-layout guard. |
| 2 | P0 | Manual re-implementation | [ ] | Frontend custom layout menu wiring | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/bloomEditing.ts`, `src/BloomBrowserUI/bookEdit/toolbox/canvas/*` | Restore custom cover toggle flow from edit view into current registry/tooling architecture. Depends on items 1, 20. Includes adding `import { setupPageLayoutMenu }` and calling it from `OneTimeSetup()`. |
| 3 | P0 | Manual re-implementation | [ ] | Language submenu for canvas text | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementContextControls.tsx`, `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlRegistry.ts` | Add language switching behavior for translation groups, including missing-editable clone path and class normalization. |
| 4 | P0 | Manual re-implementation | [ ] | Field Type submenu for custom pages | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementContextControls.tsx`, `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlRegistry.ts` | Reintroduce data-book/data-derived conversions and rethink trigger behavior. |
| 5 | P0 | Manual re-implementation | [ ] | Image field type / become background behavior | `25d0286ca`, `761866a8e` | `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlRegistry.ts`, `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementBackgroundImageManager.ts` | Restore `Cover Image` + `Become Background`, including post-review `data-book` demotion fix. |
| 6 | P1 | Manual re-implementation | [ ] | Canvas tool gating for custom page exception | `761866a8e` | `src/BloomBrowserUI/bookEdit/toolbox/canvas/CanvasToolControls.tsx` | Rename `isXmatter`→`pageTypeForbidsCanvasTools`, use `isXmatter({ returnFalseForCustomPage: true })`. Depends on item 19. |
| 7 | P0 | Manual re-implementation | [ ] | Origami-created canvas structure | `3ea41cd47` | `src/BloomBrowserUI/bookEdit/js/origami.ts` | New origami canvas insertion should include `.bloom-canvas-element.bloom-backgroundImage > .bloom-imageContainer > img`. Also add `bloom-has-canvas-element` class to the `.bloom-canvas` div. |
| 8 | P1 | Manual re-implementation | [ ] | Background image setup/sizing/order robustness | `3ea41cd47` | `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementBackgroundImageManager.ts` | Ensure background element setup runs consistently and hidden-state typo is corrected (`hidden`, not `none`). |
| 9 | P1 | Merge/port carefully | [ ] | Drag/drop coordinate consistency | `25d0286ca` | `src/BloomBrowserUI/bookEdit/toolbox/canvas/CanvasElementItem.tsx`, `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementManager.ts`, `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementFactories.ts` | Verify client/screen coordinate handling stays correct after refactor and matches intended drop placement. |
| 10 | P1 | Manual re-implementation | [ ] | Text color targeting for derived fields | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementManager.ts` | Color controls should include `[data-derived]` text targets when no `bloom-editable` is present. |
| 11 | P0 | Manual re-implementation | [ ] | Paste side-effects in bloomEditing.ts | `0246dd3f4`, `390a5eda6`, `0ebbc6cb7` | `src/BloomBrowserUI/bookEdit/js/bloomEditing.ts` | Restore delayed `wrapWithRequestPageContentDelay` wrapping around `pasteImpl`, robust Ctrl+V detection (`e.key?.toLowerCase()` + `e.code === "KeyV"`), and `scheduleMarkupUpdateAfterPaste()` calls at all three paste exit points. Port final master behavior, not just the listed commits. Depends on item 17 for `scheduleMarkupUpdateAfterPaste` export. Checklist: [ ] Verify final file state against `master` before marking done. |
| 12 | P1 | Manual re-implementation | [ ] | Source bubble recompute and qtip cleanup | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/bloomEditing.ts`, `src/BloomBrowserUI/bookEdit/sourceBubbles/BloomSourceBubbles.tsx` | Restore `recomputeSourceBubblesForPage()` (extracted `prepareSourceAndHintBubbles` + `makeSourceBubblesIntoQtips`). Also port generalized `removeSourceBubbles` to use `[data-hasqtip]` selector instead of only `.bloom-translationGroup`. |
| 13 | P1 | Normal merge/cherry-pick + resolve | [x] | pageList iframe URL update on rename | `ac2777c3b` | `src/BloomBrowserUI/bookEdit/editViewFrame.ts`, `src/BloomExe/Edit/EditingView.cs`, `src/BloomExe/Edit/EditingModel.cs` | Add `switchThumbnailPage` and C# caller after rename/update page list URL. Include the related page-list URL encoding/update from `EditingModel.cs` when not cherry-picking the whole commit. Checklist: [ ] Verify final file state against `master` before marking done. |
| 14 | P1 | Normal merge/cherry-pick + resolve | [x] | Top bar browser-click hookup migration | `1aa782950` | `src/BloomExe/Edit/EditingView.cs` | Move `_editControlsReactControl.OnBrowserClick` subscriptions to `CommonApi.WorkspaceView.TopBarReactControl.OnBrowserClick`. Also removes `TopBarControl` property, `WidthToReserveForTopBarControl`, `PlaceTopBarControl()`, and `_topBarPanel_Click`. |
| 15 | P1 | Normal merge/cherry-pick + resolve | [ ] | LicenseInfo type migration | `6aa82e812`, `119f8cf0b` | `src/BloomExe/web/controllers/CopyrightAndLicenseApi.cs`, `src/BloomExe/Edit/EditingView.cs` | Keep final master state (`CreativeCommonsLicenseInfo` etc.) while preserving branch-specific behavior; there was revert/reapply churn in this area, so prefer final-file-state verification over replaying commits blindly. Checklist: [ ] Verify final file state against `master` before marking done. |
| 16 | P2 | Do not port | [ ] | TOC grid revert pair | `7e7e66a32` + `79c2310d4` | N/A | Explicitly ignore; net master behavior is no TOC-grid change from this pair. |
| 17 | P0 | Normal merge (no branch changes) | [x] | Paste infrastructure in toolbox.ts | `0246dd3f4`, `390a5eda6`, `0ebbc6cb7`, `e0527a13a` (+ follow-up edits in master) | `src/BloomBrowserUI/bookEdit/toolbox/toolbox.ts` | **NEW.** Branch never changed `toolbox.ts`. Bulk of paste side-effects infra landed across these commits with additional follow-ups in master. Port the **final master state** (not each incremental commit list). Exports `scheduleMarkupUpdateAfterPaste`. Checklist: [ ] Verify final file state against `master` before marking done. |
| 18 | P1 | Normal merge (no branch changes) | [x] | Paste init in toolboxBootstrap.ts | `0246dd3f4` | `src/BloomBrowserUI/bookEdit/toolbox/toolboxBootstrap.ts` | **NEW.** Branch never changed this file. Small paste-related initialization addition. |
| 19 | P1 | Normal merge (no branch changes) | [x] | isXmatter custom-page arg in toolboxToolReactAdaptor.tsx | `25d0286ca`, `761866a8e` | `src/BloomBrowserUI/bookEdit/toolbox/toolboxToolReactAdaptor.tsx` | **NEW.** Branch never changed this file. Adds `{returnFalseForCustomPage}` option to `isXmatter()`. Prerequisite for item 6. |
| 20 | P0 | Copy from master (new files) | [ ] | New files: customPageLayoutMenu.tsx + customXmatterPage.tsx | `25d0286ca`, `761866a8e` | `src/BloomBrowserUI/bookEdit/toolbox/canvas/customPageLayoutMenu.tsx`, `src/BloomBrowserUI/bookEdit/toolbox/canvas/customXmatterPage.tsx` | **NEW.** These files don't exist on the branch. Copy from master HEAD. Prerequisite for items 2, 6. |
| 21 | P2 | Normal merge (no branch changes) | [x] | editMode.less CSS additions | `25d0286ca`, `535cdfff36` | `src/BloomBrowserUI/bookEdit/css/editMode.less` | **NEW.** Branch never changed this file. Canvas-specific and video CSS additions. |
| 22 | P2 | Normal merge (trivial) | [ ] | bloomVideo.ts comment update | `535cdfff36` | `src/BloomBrowserUI/bookEdit/js/bloomVideo.ts` | **NEW.** 2-line comment edit about draggable video controls. Both sides modified this file but changes are in different areas — likely auto-mergeable. |
| 23 | P2 | Manual (during other items) | [ ] | bloomEditing.ts type-safety lint cleanups | `25d0286ca` | `src/BloomBrowserUI/bookEdit/js/bloomEditing.ts` | **NEW.** Master made many minor cleanups: `== null` → `=== null`, unused-param `_` prefixes, `catch (e)` → `catch`, removed `String` interface augment, typed `undoManager` casts. Fold these in while working on items 2/11/12. |

## Risk Flags
- **Item 11 is harder than it looks** — master evolved the paste approach 4 times. The final state in `toolbox.ts` is substantially different from the first attempt. Port the final master state, not each incremental commit.
- **Items 3-5 are the riskiest** — the upstream language/field-type/become-background submenus were added to the old monolithic `CanvasElementContextControls.tsx` which no longer exists on this branch (split into `canvasElementManager/` modules). These need careful translation into the registry pattern.
- **`bloomEditing.ts` has the most diverse overlap** — paste, source bubbles, type cleanups, custom page layout hookup. Consider splitting its work across items 2/11/12/23 rather than treating it as one merge.

## Backend Overlap Note
Backend files were changed on this branch too, but not as part of the frontend canvas-manager refactor split:
- `src/BloomExe/Edit/EditingView.cs`
- `src/BloomExe/web/controllers/CopyrightAndLicenseApi.cs`
- `src/BloomExe/web/controllers/EditingViewApi.cs`

Because these are still overlap files, use normal merge/cherry-pick where possible, then resolve line-level conflicts once, instead of manually re-implementing backend behavior from scratch.

After each item, lint and fix things, have a subagent review, run tests where they are relevant, and then make a commit. THen move on to the next step.
