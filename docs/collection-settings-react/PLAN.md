# Collection Settings in React with Config-R (BL-16271)

*Written 2026-09-21, pared down 2026-10-06 after the shell card (BL-16902, PR #8388). Sources:
BL-16271 and its comments, the seven "Settings:" cards BL-16733 to BL-16739 with their mockup
screenshots (2026-08-21, follow-ups 2026-09-16), and the existing WinForms dialog.*

**How to use this plan.** It says what we are building and why, what has been decided, and what
must not get lost. It is not an implementation spec. Each card designs its own implementation;
anything below that names existing code is a place to start looking, not an instruction. Two
tests for any design: how many places must one more setting be written (the target is the C#
property, the TS type and the Config-R control), and is any piece there only because the WinForms
dialog had it.

## 1. The goal

Replace the WinForms Collection Settings dialog (a six-tab `TabControl`, two tabs WinForms, four
hosting React) with one React dialog: a `ConfigrPane` in a `BloomDialog` with seven pages,
**Languages, Front & Back Matter, Subscription, Team Collection, Bloom Library, Advanced,
Experimental**. Both dialogs coexist on alpha behind two toolbar buttons until the last page
lands; then the old one goes (step 8). Languages is the only page with substantial new UI; the
rest reuse data and, where they exist, components we already have.

## 2. What the mockups changed

Where the mockup and current code disagree and the code is newer, keep the code.

| Current dialog | Mockup (Aug 2026) | Read as |
|---|---|---|
| Languages: L1, L2, **L3**, Sign Language; fonts on Book Making; script settings in `ScriptSettingsDialog` | L1, L2, Sign Language cards with Language, Default Font, and a "More" sub-page (Fonts, Other, Script). No L3. | "More" absorbs `ScriptSettingsDialog`. Missing L3 is not a removal (Hatton, 2026-09-16: "pretty pictures, not running code"): **keep L3.** |
| Page Numbering Style on Book Making | Inside each language's "More > Script" | See Q5. |
| No UI Font, Common Name, Alphabet, Keyboard, Sentence ending punctuation in collection settings | All five in "More" | New features, not a port: see Q6. |
| Book Making: fonts, page numbering, xmatter list, bookshelf | Tab gone; xmatter becomes a select with description on **Front & Back Matter**; bookshelf moves to **Bloom Library** | Intent. |
| Project Information: Country, Province, District, Collection Name | Places group on Front & Back Matter; Collection Name on **Advanced** (disabled in a Team Collection) | Intent. |
| Advanced: auto update, QR codes, experimental features | Advanced: auto update, collection name. QR codes move to Front & Back Matter with a new live badge preview. Experimental features get their own page. | Intent; the QR preview is new UI. |
| Subscription, Team Collection tabs (React) | Same content | Reuse. |
| Restart reminder, OK becomes "Restart"; Help button per tab | Not shown (mockups have no buttons) | Unspecified; see Q7, Q8. |
| Older Notion design: Appearance tab (collection defaults for Book Settings), AI tab | Absent | Dropped from this project (Q2). |

The 2023 cards BL-12408, BL-12416, BL-12417, BL-12418 are superseded by BL-16733 to BL-16739.

## 3. The framework as built (BL-16902)

What a page card builds on:

- **Launch.** `showCollectionSettingsDialog(pageKey?)` raises the dialog's `LaunchDialog` event in
  the browser; `App.tsx` renders the dialog, so it opens from any workspace tab, optionally on a
  named page. No C# launch endpoint and no feature flag (Q4).
- **Data.** `GET collection/settings` returns `{ values, restartPaths, isTeamCollection }`, or only
  `notAllowedMessage` for a Team Collection member who is not an administrator (shown in a small
  dialog with Close). OK posts `{ values, restartRequired }` once; Cancel posts nothing.
- **One place per setting.** `CollectionSettingsValues` (`CollectionSettingsTypes.cs`) is a live
  view of `CollectionSettings`: a simple setting is one property that reads and writes it, marked
  `[RequiresRestart]` if a change needs a restart (the restart paths are built from those marks).
  Adding one means that property, the TS type (`collectionSettingsTypes.ts`) and the Config-R
  control. The POST populates the view from the posted JSON, then `CollectionSettingsUpdater.Apply`
  runs the rules that span values and saves.
- **Rules that span values** live in `CollectionSettingsUpdater`: the languages (applied together,
  reusing `UpdateLanguageSettings`), the xmatter check, and, ported from the WinForms OK handler
  and waiting for their cards, `ApplySubscriptionAndBookshelf` (Pro tier refused in a Team
  Collection; the BL-15056 expired-bookshelf cases) and `ApplyAdministrators`. `Apply` already
  calls the last two with "unchanged" on every save, which keeps an expired subscription's
  bookshelf in the file.
- **Pages.** Each built page is a hook in its own file under `collection/settingsPages/` that
  returns its `ConfigrPage` (Config-R throws, blanking the UI, if a pane's child is not a
  `ConfigrPage` or a page's child is not a `ConfigrGroup`, so a page cannot be a component of its
  own). `ExperimentalPage.tsx` is the first; each card moves its page out of the placeholders in
  `CollectionSettingsDialog.tsx` the same way.
- **Validation and clean-up belong in the client** (Config-R can validate a field; it does not
  trim, so add that to Config-R if a page needs it). The POST has no validation reply.
- **Restart.** The dialog compares `restartPaths`, shows the restart reminder and relabels OK; C#
  reopens the collection after replying.
- **Saving.** OK is disabled while a save is in flight; a failed save re-enables it and goes to
  Bloom's normal error reporting. Two theoretical races are deliberately unguarded; comments in
  `CollectionSettingsDialog.tsx` say why.
- **Chrome.** Both Config-R settings dialogs (this and Book Settings) use the usual `BloomDialog`
  shape and share `ConfigrDialogMiddle` and the 900×720 size (`react_components/ConfigrDialogMiddle.tsx`).
  Search is off.
- **The WinForms dialog is untouched**, including `DialogBeingEdited` and the `settings/*` endpoints
  its React tabs post to. The two save paths share nothing.
- **Strings.** The new page labels are `translate="no"` until the pages are real; reuse existing
  `CollectionSettingsDialog.*` ids where the text still fits.

**Known gap for reused components.** The Subscription, Team Collection and bookshelf components
post their edits to `settings/*` endpoints that only feed the WinForms dialog. The first card that
reuses one decides how its edits reach the React POST.

## 4. Decisions

**Decided (John Thomson, 2026-09-22):**

- **Q1 Save model:** everything on OK, like today; Cancel abandons.
- **Q2 Appearance tab** (collection defaults for Book Settings, BL-12521): out of scope.
- **Q3 Languages "More" sub-page:** implement sub-pages in Config-R (BL-13273). Config-R
  alpha.27 has none (`ConfigrPage` children may only be groups).
- **Q4 Cutover:** no flag; everyone on master sees both buttons; the old one goes before beta.

**Tentative (John, 2026-09-22; may be overridden):**

- **Q5 Page Numbering Style:** stays per collection, on Front & Back Matter next to Style.
- **Q6 New per-language fields:** UI Font, Keyboard, Common Name are out (no backing data).
  Alphabet and Sentence ending punctuation are in BL-16739; they are reader-tool settings stored
  per language in `ReaderToolsSettings-<tag>.json` under app data, not in the collection, so
  moving them is a data change. Ask Hatton whether these were meant for 6.6.
- **Q7 Restart reminder and OK→Restart:** keep (built).
- **Q8 Help:** one Help button for the selected page. Config-R does not report the current page,
  so this needs a way to know it (track nav clicks, or add `onPageChange` to Config-R).
- **Q9 Experimental page:** a list of the features that exist (only Team Collections today);
  each unmerged feature adds itself when it lands. Experimental book sources is dead (turned off
  at every startup, never shown) and stays out.
- **Q10 Subscription gating on pages:** the badge beside the control, as in Book Settings.
- **Q11 Language "Change…":** the ethnolib chooser in a nested React dialog.
- **Q12 Creating a Team Collection from the dialog:** a pending change applied on OK, so Cancel
  abandons it (today it restarts immediately).
- **Q13 Size:** fixed 900×720 (built). **Q14 Rename:** behaves as today; disabled in a Team
  Collection with the existing explanation. **Q15:** no file-format migration is needed for
  anything here except Q6's fields.

## 5. Pages

Each is one card, independent once the shell is in. For each: what it contains, and what must not
get lost.

**BL-16733 Front & Back Matter.** Style (xmatter select with description, honoring a
branding-forced pack), QR Codes (show, caption, and a new live badge preview: C# builds the badge
today, so the preview needs an endpoint), Places (Country, Province, District, trimmed in the
client), Page Numbering Style (Q5). Values for xmatter, QR and places already exist in the model.

**BL-16737 Advanced.** Automatically update Bloom (a user-level setting, shown only where
supported), Collection Name (disabled in a Team Collection; the model already queues the rename;
trim in the client).

**BL-16736 Bloom Library.** The bookshelf, with subscription gating (Q10). Post it and hand it to
`ApplySubscriptionAndBookshelf`, which has the expired-bookshelf rules.

**BL-16734 Subscription.** Reuse `SubscriptionSettings`. Hand the entered subscription to
`ApplySubscriptionAndBookshelf` (its descriptor-change bookshelf clearing never fires; BL-16904).
Reset its preview on Cancel. The "fix invalid branding" startup path must open this page. Decide
whether the other pages' subscription gating (Experimental's Team Collections box, the bookshelf)
should follow a code typed here but not yet saved; `features/status` only knows the saved one. If
so, replace the Experimental page's own `useGetFeatureStatus` call with a small dialog-level
subscription lookup that every page reads.

**BL-16735 Team Collection.** Reuse `TeamCollectionSettingsPanel` with its overlay and warning.
Validate administrator emails in the client (`CollectionSettings.ValidateAdministrators` has the
rules) and hand them to `ApplyAdministrators` (`Apply` calls it with null today, which does
nothing; it is there for this card). Hide the page when the feature is off and this is
not a TC, and when editing a Bloom Library book (as `CollectionSettingsDialog.cs` does). Creating
a TC per Q12.

**BL-16739 Languages.** The large one. L1, L2, L3, Sign Language: name with "Change…" (Q11),
Default Font, "Remove" for L3 and Sign Language, and "More" (Q3) with fonts, line spacing, tool
font size, Asian breaking and RTL, plus Q6's reader-tool fields. Retires `ScriptSettingsDialog`
and the Book Making font controls. Could split into rows-and-fonts, then "More". Note:
`UpdateLanguageSettings` still takes the fonts as a separate array, a WinForms leftover (fonts
were on another tab there); in the values the font is already part of each language.

**BL-16738 Experimental.** Per Q9. Built: a feature that lands adds an `ExperimentalFeatureSetting`
row in `settingsPages/ExperimentalPage.tsx` and a property in `ExperimentalValues`.

## 6. Cutover (new card)

- First go through the WinForms OK handler (`_okButton_Click`) line by line and confirm each rule
  exists in the React path or was dropped on purpose.
- Remove the old button, the WinForms dialog, `ScriptSettingsDialog`, the four `ReactControl` tab
  bundles and their vite entries, `DialogBeingEdited`, and the legacy launch path. The new save
  path still reads `CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform` (in
  `AdvancedValues`), so that flag needs a new home when the WinForms class goes.
- Point the "open settings on the Subscription page" callers (subscription badges, menu items,
  `CheckForInvalidBranding`) at the new dialog. A caller in an iframe reaches the top frame's
  `showCollectionSettingsDialog` through the workspace bundle's exports, as `showBookSettingsDialog`
  does; the Comic Book ReadMe links are `fetch`es in book content, so they keep an endpoint.
- Mark unused `CollectionSettingsDialog.*` strings obsolete (never delete), update the help
  mapping and help pages, add an e2e test that opens the dialog and changes a setting (and update
  `src/BloomE2E/AUTOMATION-DEBT.md`), post test ideas on BL-16271.

## 7. Risks

- **Two save paths until cutover.** A fix to a rule both dialogs have must go into both.
- **Config-R renders custom controls by identity.** A `control` that is not `useCallback`-stable
  remounts on every render; every reused component wrapped for Config-R needs this.
- **Reused components assume they own a tab.** The Subscription component refreshes on a DOM
  event and is styled for the old tab; expect layout work.
