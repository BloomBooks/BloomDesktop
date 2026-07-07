# Canvas Tool Manual Test Scripts (Headed, CURRENTPAGE)

This was an experiment to see what test scripts gpt 5.3-codex could come up with. It wasn't a very good result;
it's at best a kind of smoke test. I think more prompting/skill/agent whatever
would be needed.

## Important test conventions
- Always use the **...** button in context controls to open menus (not right-click).
- Prefer behavior checks (enabled/disabled, element count, command availability, dialog opens) over “no crash.”
- Keep a running note of anything odd (sticky menus, unexpected disable states, wrong controls by element type).

## Observed baseline from this exploration pass
- Dragging **Speech** selected the new element and set toolbox style to **Speech** with **Show Tail** checked.
- **Add child bubble** added an extra editable; duplicating a parent bubble duplicated child structure too.
- For placeholder images, **Copy image** and **Set image information...** were disabled.
- For non-placeholder image src, **Copy image** and **Set image information...** became enabled.
- **Navigation Image+Label** showed only `Text Color` + `Background Color` controls.
- **Navigation Image** showed only `Background Color`.
- **Navigation Label** showed `Text Color` + `Background Color`, with no image commands.
- **Video** menu included `Choose video`, `Record yourself`, `Play Earlier`, `Play Later`; with a single video, earlier/later were disabled.
- **Book Link Grid** showed toolbar `Choose books...` and opened **Book Grid Setup** dialog.
- **Set Destination** on navigation buttons opened **Choose Link Target** dialog.

## Clipboard prep for image-state checks

### Option A (manual)
1. Copy a PNG/JPG from Snipping Tool, Paint, or another image app.
2. Select an image canvas element.
3. Open **...** and run **Paste image**.

### Option B (browser console helper)
1. In devtools console, run a script that writes a generated PNG via `navigator.clipboard.write([new ClipboardItem({"image/png": blob})])`.
2. Verify script returns success.
3. Run **Paste image** from the image element menu.

---

## Script 01: Frame + tool readiness
1. Open `CURRENTPAGE`.
2. Confirm toolbox iframe, page list iframe, and page iframe are present.
3. Activate **Canvas Tool** tab if needed.
4. Confirm canvas control area is visible.
5. Confirm page canvas is visible and interactive.

## Script 02: Main palette drag creation
1. Drag **Speech**, **Image**, **Video**, **Text Block**, **Caption** to canvas.
2. Drop each at distinct points.
3. Verify count increases by 5 total.
4. Re-select each dropped element.
5. Verify controls/menu contract matches element type.

## Script 03: Navigation palette drag creation
1. Expand **Navigation** section.
2. Drag **Image+Label Button**, **Image Button**, **Label Button**, **Book Link Grid**.
3. Verify each drop creates one element.
4. Re-select each and verify type-specific controls.
5. Note any element that gets wrong control set.

## Script 04: Speech bubble style cycle
1. Select a speech/text-capable bubble.
2. Open style dropdown.
3. Select: `Caption`, `Exclamation`, `Just Text`, `Speech`, `Ellipse`, `Thought`, `Circle`, `Rectangle`.
4. Verify shape/style updates each time.
5. Verify controls stay stable (no disappearing mandatory controls).

## Script 05: Show Tail behavior
1. Select a bubble that supports tails.
2. Toggle **Show Tail** off/on.
3. Verify visual tail change.
4. Open **...** and check text/bubble commands still present.
5. Re-select and confirm setting persisted.

## Script 06: Rounded corners eligibility
1. Select a bubble and capture current rounded-corners enabled state.
2. Change style/background states that should affect eligibility.
3. Verify checkbox enable/disable transitions.
4. When enabled, toggle it on/off and confirm visual change.
5. Record any state where eligibility appears incorrect.

## Script 07: Text color behavior
1. Select text-capable element.
2. Pick non-default text color.
3. Verify rendered text color changes.
4. Revert to default/inherited.
5. Verify color returns to style default.

## Script 08: Background color behavior
1. Select element with background color support.
2. Apply visible background color.
3. Verify fill appears.
4. Return to transparent/default.
5. Verify fill and any dependent controls update consistently.

## Script 09: Outline color behavior
1. Select element with outline dropdown.
2. Iterate all outline values including `None`.
3. Verify outline appearance updates.
4. Duplicate element.
5. Verify chosen outline on duplicate vs original is sensible.

## Script 10: Bubble child lifecycle
1. Select speech bubble.
2. Open **...** and run **Add child bubble** three times.
3. Delete one child.
4. Add another child.
5. Delete parent and verify child cleanup behavior.

## Script 11: Bubble duplicate with children
1. Create parent+child bubble structure.
2. Duplicate parent from **...**.
3. Verify duplicate appears.
4. Verify child structure is duplicated (not dropped).
5. Delete duplicate and verify original remains stable.

## Script 12: Text commands in menu
1. Select text-capable non-button element.
2. Open **...**.
3. Run **Format text...** and close dialog.
4. Run **Copy text** and **Paste text** into another text element.
5. Verify content transfer and no unrelated style/position mutation.

## Script 13: Auto height command
1. Select text-capable non-button element.
2. Add multiline content.
3. Run **Auto height** from **...**.
4. Verify element resizes to fit content.
5. Remove text, run again, and verify shrink behavior is sane.

## Script 14: Image placeholder state contract
1. Select placeholder image element.
2. Open **...**.
3. Verify `Copy image` disabled.
4. Verify `Set image information...` disabled.
5. Verify `Reset image` disabled when no crop exists.

## Script 15: Image non-placeholder state contract
1. Set image to non-placeholder (paste image or set src via harness helper).
2. Re-open **...**.
3. Verify `Copy image` enabled.
4. Verify `Set image information...` enabled.
5. Verify `Reset image` still disabled unless cropped.

## Script 16: Image duplicate/delete flow
1. Select image element.
2. Open **...** and run **Duplicate**.
3. Verify element count +1.
4. Re-open **...** on duplicate and run **Delete**.
5. Verify count returns and selection remains valid.

## Script 17: Video menu contract
1. Select video element.
2. Open **...**.
3. Verify `Choose video from your computer...` and `Record yourself...` exist.
4. Verify `Play Earlier` / `Play Later` exist.
5. With only one video, verify earlier/later are disabled.

## Script 18: Video ordering commands
1. Create at least two video elements.
2. Select one and open **...**.
3. Run **Play Earlier** or **Play Later**.
4. Verify command enablement changes at boundaries.
5. Verify ordering behavior is reflected consistently.

## Script 19: Navigation Image+Label controls
1. Select **Image+Label Button**.
2. Verify toolbox shows only `Text Color` and `Background Color` controls.
3. Open **...**.
4. Verify menu includes destination + image + text command groups.
5. Confirm duplicate/delete present.

## Script 20: Navigation Image controls
1. Select **Image Button**.
2. Verify toolbox shows only `Background Color`.
3. Open **...**.
4. Verify image commands are present, text commands absent.
5. Confirm duplicate/delete present.

## Script 21: Navigation Label controls
1. Select **Label Button**.
2. Verify toolbox shows `Text Color` + `Background Color`.
3. Open **...**.
4. Verify text commands are present, image commands absent.
5. Confirm duplicate/delete present.

## Script 22: Set Destination dialog wiring
1. On any navigation button, open **...**.
2. Run **Set Destination**.
3. Verify **Choose Link Target** dialog appears.
4. Dismiss dialog (Cancel/Close/Escape).
5. Verify canvas selection/editing resumes cleanly.

## Script 23: Book Link Grid toolbar flow
1. Select **Book Link Grid** element.
2. Verify toolbar shows `Choose books...` affordance.
3. Click `Choose books...`.
4. Verify **Book Grid Setup** dialog appears.
5. Dismiss and verify element remains selectable/editable.

## Script 24: Book Link Grid menu flow
1. Select **Book Link Grid** element.
2. Open **...**.
3. Verify menu contains `Choose books...`.
4. Run command and dismiss dialog.
5. Verify command remains available after dismissal.

## Script 25: Mixed duplication integrity
1. Create one each: speech/text, image, video, navigation button.
2. Duplicate each where allowed.
3. Mutate duplicate (text/content/color/image if available).
4. Verify original did not change unintentionally.
5. Verify delete on duplicate does not affect original.

## Script 26: Delete handoff behavior
1. Select a middle element among several.
2. Delete via **...**.
3. Verify deterministic next selection (or none).
4. Repeat on first and last element.
5. Note any inconsistent focus/selection behavior.

## Script 27: Move + resize handles
1. Select an element with visible selection frame.
2. Drag to new location.
3. Resize from all four corners.
4. Resize from side handles.
5. Verify element remains visible and selectable.

## Script 28: Keyboard movement
1. Select one element and record its position.
2. Press arrow key once.
3. Verify movement in expected direction.
4. Press `Ctrl+Arrow` and compare delta.
5. Verify no unexpected menu focus steals keyboard movement.

## Script 29: Cross-type menu sanity sweep
1. For each major type (speech, image, video, nav variants, link-grid), open **...**.
2. Record command list.
3. Compare against expected type-specific contract.
4. Flag missing or extra commands.
5. Re-test any suspicious type after reselection.

## Script 30: End-to-end regression pass
1. On one page, create at least six mixed element types.
2. For each, run one mutation command and one structural command (duplicate/delete).
3. Run one dialog command (`Set Destination` or `Choose books...`) and dismiss it.
4. Re-select each remaining element and verify toolbox controls match type.
5. Confirm page remains editable with no stuck overlays.

---

## Explicit exclusions for this suite
- Do **not** run **Change Image**.
- Do **not** run **Choose image from your computer...**.
