# Collection Settings in React with Config-R (BL-16271)

*Plan written 2026-09-21 against master. Sources: card BL-16271 and its comments; the seven
"Settings:" cards BL-16733 to BL-16739 and their mockup screenshots (2026-08-21, with follow-up
comments of 2026-09-16); the React skeleton in `src/BloomBrowserUI/collection/CollectionSettingsDialog.tsx`;
the existing WinForms dialog and its history since 2025; and the Config-R 1.0.0-alpha.27 API we
have pinned. The Notion mockup mentioned on the card was not reachable from this session; the
screenshots on the sub-cards are the newest design we have, and this plan treats them as
superseding the older Notion design summarized in the May 2026 comment on BL-16271.*

## 1. The picture in one paragraph

The current dialog is a WinForms `TabControl` with six tabs, 652×572 and not maximizable. Two are still WinForms
(Languages, Project Information); four already host React (`Book Making`, `Bloom Subscription`,
`Team Collection`, `Advanced Program Settings`), and Advanced is already Config-R. Every React
tab pushes its edits, as they happen, into `Pending*` fields on the live dialog through a static
`CollectionSettingsApi.DialogBeingEdited`, and the dialog's OK handler
(`CollectionSettingsDialog.cs:387-563`) applies them all to `CollectionSettings`, saves, and
returns `DialogResult.Yes` when a restart is needed. The new dialog is a single `ConfigrPane`
inside a `BloomDialog`, launched from the collections tab over the websocket, holding seven
pages: **Languages, Front & Back Matter, Subscription, Team Collection, Bloom Library,
Advanced, Experimental**. Most of the work is (a) moving the OK-time apply logic out of the
WinForms class so both dialogs can share it, (b) rehoming the three React tabs that exist, and
(c) building Languages, which is the only page with real new UI.

## 2. What the mockups changed relative to today

This is the reconciliation the card asked for: which differences are design intent and which
are just the mockup being older than the code. Where the current code is newer, the plan keeps
the code.

| Current dialog | Mockup (Aug 2026) | Read as |
|---|---|---|
| Languages tab: L1, L2, **L3**, Sign Language, each name + "Change…"; fonts on Book Making; RTL / line spacing / Asian breaking / UI font size in the WinForms `ScriptSettingsDialog` | L1, L2, Sign Language cards, each with Language, Default Font, and a "More" sub-page (Fonts, Other, Script groups). No L3. | Sub-page absorbs `ScriptSettingsDialog`: intent. Missing L3: Hatton's 2026-09-16 reply ("pretty pictures, not running code") means do not infer removal. **Keep L3.** |
| Page Numbering Style on Book Making (one per collection) | Inside each language's "More > Script" group | Ambiguous: see Q5. |
| No per-language "UI Font", "Common Name", "Alphabet", "Keyboard", "Sentence ending punctuation" in collection settings (Alphabet and sentence punctuation live in the reader-tools settings JSON per language; the others do not exist) | All five appear in "More" | New features, not a port: see Q6. |
| Book Making tab: fonts, page numbering, xmatter chooser (list), bookshelf | Tab gone. Xmatter becomes a `Select` with description on **Front & Back Matter**; bookshelf moves to **Bloom Library**. | Intent. |
| Project Information tab: Country, Province, District, Collection Name | Country/Province/District become the **Places** group on Front & Back Matter; Collection Name moves to **Advanced > Collection** (disabled in a Team Collection, per John's 2026-09-16 comment) | Intent. |
| Advanced tab (Config-R): Program (auto update), QR Codes (show + caption), Experimental Features (Team Collections, plus a normally hidden Experimental Book Sources toggle; App Builder and AI removed by BL-16731) | **Advanced**: Program (auto update), Collection (name). QR Codes move to Front & Back Matter and gain a live badge preview. Experimental features get their own **Experimental** page. | Intent; the QR preview image is new UI. Mockup's Experimental list (Cloud Sync, Tables, Picture with Text Wrapping) names features not yet merged (branches `BL-16818-tables*`, `cloud-collections`). |
| Bloom Subscription tab (React) | **Subscription**: same component, less padding | Reuse as-is (card says so). Current code is newer (BL-16786, BL-16885). |
| Team Collection tab (React, with `RequiresSubscriptionOverlayWrapper`) | **Team Collection**: same content including the experimental warning box | Reuse as-is (card says so). |
| Restart reminder + OK becomes "Restart" | Not shown | Mockups have no bottom buttons at all; treat as unspecified. See Q7. |
| Help button per tab | Not shown | See Q8. |
| Older Notion design (May 2026 comment): **Appearance** tab of collection-wide defaults for Book Settings, inherit/override groups, an **AI** tab | Neither exists in the Aug 2026 cards | Treat as dropped from this project unless Hatton says otherwise: Q2. |

Old 2023 cards BL-12408, BL-12416, BL-12417, BL-12418 ("… Collection Settings --> Web", Figma
links) describe the same per-tab work and are superseded by BL-16733 to BL-16739; BL-12407 was
already closed as a duplicate of BL-16271. Recommend closing the other four the same way.

## 3. Getting the stub to a good starting point

The skeleton (`collection/CollectionSettingsDialog.tsx`, 155 lines, last touched 2026-04-06) is
the right shape and worth keeping: `BloomDialog` shell, `useEventLaunchedBloomDialog`, a
`ConfigrPane` fed from `GET collection/settings`, edits captured through `onChange`, one
`POST collection/settings` on OK. It is mounted in `CollectionsTabPane.tsx:753` and waits for a
`LaunchDialog("CollectionSettingsDialog")` that C# never sends (`WorkspaceApi.cs:60-61` is commented
out). The C# endpoint is a placeholder (`CollectionSettingsApi.cs:52-67`: GET returns `{}`).

The stub becomes a usable base when these are done. This is a real unit of work, tracked as
**BL-16902 "Settings: dialog shell and save pipeline"**, the first sub-card of BL-16271 (the Aug
comment on the card assumed the stub already did this; it does not).

### 3.1 Front end

1. **Pages.** Replace the two placeholder pages (`languages`, `appearance`) with the seven pages
   of the mockup, in mockup order, each holding a `ConfigrStatic` placeholder until its card lands.
   Use `topLevel` `ConfigrPage`s (flat list, as the mockups show), not `ConfigrArea`. Delete the
   `appearance` page unless Q2 revives it. Localize the page labels; reuse existing ids where the
   text is unchanged (`CollectionSettingsDialog.LanguageTab.LanguageTabLabel`,
   `Common.BloomSubscription`, `TeamCollection.TeamCollection`) and add new ones for the rest.
2. **Shell.** Fixed size like Book Settings (`kBookSettingsDialogWidthPx`/`Height` in
   `BookAndPageSettingsDialog.tsx:42-43`; copy the `DialogMiddle` css there that gives Config-R's
   internal `form`/`#groups` a scrolling height). `showSearch={false}` (the mockups have no
   search box and Config-R's README says search "has not made it to production yet").
3. **Deep link.** Accept an initial page key (`initiallySelectedTopLevelPageKey`) from the launch
   event's parameters, so `common/showSettingsDialog?tab=subscription` (posted by the subscription
   badges via `featureStatus.ts`, by `localizableMenuItem.tsx`, and hard-coded in the two Comic
   Book template ReadMes) and `WorkspaceView.CheckForInvalidBranding` can open the Subscription
   page directly.
4. **Save/cancel.** Keep the "latest values in a ref, post once on OK" pattern from
   `BookAndPageSettingsDialog.tsx:492-524` (Config-R can call `onChange` during render; defer
   state updates). Post only if values changed (JSON compare). Cancel posts nothing.
5. **Restart.** OK posts, reads `{restartRequired}` from the reply, and closes; C# performs the
   reopen. Before OK, show the existing "Bloom will close and re-open…" text
   (`CollectionSettingsDialog.RestartMessage`) and relabel OK as "Restart"
   (`CollectionSettingsDialog.Restart`) when the current values differ from the initial ones in a
   key C# has flagged as restart-worthy. Simplest: the GET reply includes
   `restartKeys: string[]`; the TS compares. Everything the WinForms dialog treats as a restart
   trigger is listed in `CollectionSettingsDialog.cs` (`ChangeThatRequiresRestart` callers) and
   `CollectionSettingsApi.cs:634-704`.
6. **Launch button.** In `react_components/TopBar/CollectionTopBarControls/CollectionTopBarControls.tsx`, add the second Settings button the card
   asks for, and keep the legacy button until the cutover step. No gating (Q4): both buttons are
   visible to everyone on master. Done as `showCollectionSettingsDialog(pageKey?)`, which raises
   the dialog's `LaunchDialog` event in the browser with no trip through C#; `App.tsx` renders the
   dialog, so it opens from any workspace tab; the Team Collection administrator check lives in
   `GET collection/settings`, which returns only `notAllowedMessage` (and opens no session) for a
   non-administrator.
7. **Tests.** A vitest file modelled on `BookAndPageSettingsDialog.saving.test.tsx` covering:
   loads on open, OK posts once with changed values, Cancel posts nothing, OK label flips to
   Restart when a restart key changes.

### 3.2 Back end: a save pipeline that does not depend on WinForms

This is the piece that unblocks every tab card, and the biggest design decision in the project.

1. **One settings document.** `GET collection/settings` returns a JSON object grouped by page,
   e.g. `{languages: {...}, frontBackMatter: {xmatter, showQrCode, qrcodeCaption, country,
   province, district}, subscription: {code}, teamCollection: {administrators}, bloomLibrary:
   {bookshelf}, advanced: {autoUpdate, collectionName}, experimental: {"team-collections": true}}`
   plus the read-only context a page needs to render (xmatter offerings and the branding-forced
   xmatter, numbering styles, `showAutoUpdate`, `isTeamCollection`, `editingBlorgBook`,
   `restartKeys`). Keys are the Config-R `path`s. Serialize with a real DTO class rather than
   `dynamic` so the TS interface and C# stay in step (compare `BookSettingsApi.cs`).
2. **One apply method.** Extract the body of `_okButton_Click` (`CollectionSettingsDialog.cs:387-563`)
   into a class the WinForms dialog and the new API both call, say
   `CollectionSettingsUpdater.Apply(CollectionSettings current, PendingCollectionSettings pending)`
   returning whether a restart is needed. It already has one reusable static piece,
   `UpdateLanguageSettings` (`.cs:594-687`, unit-tested). The rest (administrator validation,
   Pro-tier-in-TC refusal, xmatter validation via `XMatterPackFinder.GetValidXmatter`, L2==L3
   clearing, collection rename event, expired-bookshelf reconciliation BL-15056, user-level
   `Settings.Default.AutoUpdate`, `ExperimentalFeatures.SetValue`) moves with it. The existing
   `UpdateLanguageSettings` tests in `src/BloomTests/Collection/WritingSystemDialogTests.cs`
   retarget to the new class, and the rest of the apply logic gets tests for the first time.
3. **Retire `DialogBeingEdited`.** Replace the static WinForms pointer with a
   `PendingCollectionSettings` session object owned by `CollectionSettingsApi`, created when
   either dialog opens and discarded on cancel. Endpoints that today write to `DialogBeingEdited`
   (`settings/setFontForLanguage`, `numberingStyle`, `xmatter`, `bookShelfData`,
   `administrators`, `advancedProgramSettings`, `subscriptionCode` via
   `SubscriptionSettingsEditorApi.NotifyPendingSubscriptionChange`) write to the session instead.
   This is what lets the reused Subscription and Team Collection components (which keep their
   own APIs) work inside the new dialog without rewriting them, and lets the WinForms dialog keep
   working during the overlap.
4. **Validation failures** (bad administrator emails, Pro tier in a TC) must be reportable
   without closing: the POST replies with a localized message and the TS shows it and keeps the
   dialog open, matching today's behaviour where OK returns early.
5. **`ScriptSettingsDialog`** stays reachable from the legacy dialog until cutover; the new
   dialog never opens it.

### 3.3 Housekeeping in the same step

- There is no Config-R conventions doc in the repo. Add a short section to
  `src/BloomBrowserUI/AGENTS.md` capturing the patterns already used three times (values via
  `onChange` ref; wrap two controls in a bare `div` to suppress the divider; `useCallback`-stable
  `control` for custom inputs; `overrideValue`/`overrideDescription` for locks; subscription badge
  beside a disabled control). The Config-R survey behind this plan lists file:line examples.
- Config-R 1.0.0-alpha.28 (2026-07-01, per the GitHub releases page) exists; its release note
  mentions only the dev entry point, so no bump is needed for this work. The vite build has no patch on config-r any more (BL-15470 is stale).
- No new xlf strings until a page card lands; when they do, reuse the `CollectionSettingsDialog.*`
  ids that still fit (72 exist; 38 are `BookMakingTab.*` and most will be marked obsolete at
  cutover, never deleted).

## 4. Open questions, prioritized

Format follows a preflight report: what is asked, what blocks on it, recommendation, alternatives.
"Blocks" names the step in §5 that cannot be finished, not started, without the answer.

### Decided 2026-09-22 (John Thomson)

Q1 to Q4 were answered before the shell card started; the recommendations below are kept for
the record and the decision is stated first in each.

**Q1. Save model: everything on OK (current behaviour), or live per-control saving?**
Decision: everything on OK, like the current dialog.
Blocks: §3.2 design; every tab card inherits the answer.
Recommendation: keep pending-until-OK with a single POST, because the restart semantics, Cancel,
and the atomic OK-time validations (administrators, Pro-in-TC, xmatter validity) all assume it,
and because Book Settings already works this way. The one Config-R tab we have
(`AdvancedSettingsPanel`) saves live to the pending store, which is compatible.
Alternative: live saving with immediate effect per setting. Would need a restart-on-close design
and an undo story for Cancel; not recommended.

**Q2. Is the collection-level Appearance tab (defaults for Book Settings, inherit/override
groups, BL-12521) out of this project?** The May 2026 Notion design had it; the Aug 2026 cards do
not; the stub still has an `appearance` page.
Decision: out of scope for now.
Blocks: whether the shell keeps the page; whether BL-12521 and the config-r inheritance work are
on the 6.6 board at all. Nothing else blocks.
Recommendation: out of scope for BL-16271. Delete the stub page and leave BL-12521 where it is
(6.7). If it comes back it is a separate epic with its own data model questions (the 2026-06-01
comment lists them: new books only vs. all non-overridden books vs. force).
Alternative: keep an empty Appearance page as a placeholder. Costs nothing but signals a
commitment we have not made.

**Q3. How does the Languages "More" sub-page get built?** Config-R alpha.27 has no sub-pages
(`ConfigrPage` children may only be groups); BL-13273 "Config-r Subpages" is open on 6.7.
Blocks: BL-16739 only.
Decision: implement sub-pages in Config-R.
Recommendation: implement sub-pages in config-r (BL-13273) if Hatton wants to own that; it is
the design the mockup draws ("← More" inside the pane) and Bloom will want it again for Book
Settings. If not, the fallback that needs no library change is a nested `BloomDialog` per
language opened from a "More…" button (a `ConfigrCustomStringInput` whose control is a button),
holding its own small `ConfigrPane` with the Fonts/Other/Script groups and writing back into the
parent's values. That is essentially today's `ScriptSettingsDialog` in React.
Alternative: the remount-with-`key` navigation trick Book Settings uses to switch pages
(`BookAndPageSettingsDialog.tsx:398-402`) plus hidden pages. Config-R has no hidden pages, so the
"More" pages would appear in the left nav; rejected.

**Q4. Cutover plan.** The card says keep the WinForms dialog "until we are done and tested" with
a second toolbar button. Who sees the second button (all users in alpha, or a flag), and does
`common/showSettingsDialog?tab=subscription` (all subscription badges) and
`CheckForInvalidBranding` switch to the new dialog at the same moment?
Blocks: step 0 (button gating) partly; step 8 fully.
Decision: no flag. Everyone building from master sees the new button next to the old one; the
whole task is finished and the old button removed before this work reaches beta.
Recommendation: gate the new button behind the experimental-features mechanism
(`ExperimentalFeatures`, token e.g. `react-settings`) so QA can turn it on per machine; route the
badge and invalid-branding entry points to whichever dialog the flag selects, so QA tests those
paths too; remove the flag, the button, and the WinForms dialog in one cutover PR.

### Answer before the corresponding tab card

Q5 to Q15 have tentative answers from John Thomson (2026-09-22), stated first in each; they
may be overridden.

**Q5. Page Numbering Style: per language (as drawn) or per collection (as stored)?**
Tentative decision: per collection for now, on the Front & Back Matter page next to Style.
Blocks: BL-16739 (and decides whether Front & Back Matter should host it instead).
Recommendation: keep one collection-wide setting; a book has one page-number style. Put it in
the Front & Back Matter page next to Style, since it is about the printed book rather than a
language. Alternative: make it per language and use L1's; that is a data-model change with a
migration and no user request behind it.

**Q6. The new per-language fields in "More": UI Font, Common Name, Alphabet, Keyboard,
Sentence ending punctuation. Are they in scope for BL-16739?**
Tentative decision: completely new fields (UI Font, Keyboard, Common Name) are out of scope.
Alphabet and Sentence ending punctuation, which have existing backing in the reader-tool
settings, are done as part of BL-16739.
Blocks: BL-16739 scope only. Fonts (Default Font), "Font size when displayed in tools"
(`BaseUIFontSizeInPoints`), "Default line spacing" (`LineHeight`), Asian word breaking
(`BreaksLinesOnlyAtSpaces`) and RTL (`IsRightToLeft`) all map to existing `WritingSystem` fields
and are in scope regardless.
Recommendation: BL-16739 implements only the existing-data fields. Alphabet and Sentence ending
punctuation are the Decodable/Leveled Reader tool settings (`ReaderSettings.letters` and
`.sentencePunct`, stored per language as `ReaderToolsSettings-<tag>.json` under
`%localappdata%/SIL/Bloom`, not in the collection folder); moving them here means the reader
tools read from collection settings and the data migrates out of app data into the
`.bloomCollection`, a separate card. Keyboard and UI Font have no backing data today; "Common
Name" overlaps the language chooser's custom-name flow (`WritingSystem.IsCustomName`) and needs
a definition. Ask Hatton
whether these were intended for 6.6 or were the prototyper filling space.

**Q7. Keep the restart reminder and OK→Restart?** Tentative decision: keep the current restart
strategy (done in BL-16902). The mockups show no buttons; the 2026-06-01
comment asks. Blocks: nothing (the shell can do it cheaply, §3.1.5). Recommendation: keep; the
behaviour is user-visible, tested by QA, and free in our own dialog shell. Restart-on-close
without warning is the alternative; the only precedent is the App Builder toggle, where a
restart prompt was added and then reverted in April 2026 (dedccf2b6d, 2f2e43c42d) with no
recorded reason.

**Q8. Keep the Help button?** Tentative decision: one Help button in the bottom bar, opening the
help page for the active Config-R page. The WinForms dialog maps each tab to a help page
(`CollectionSettingsDialog.cs:740-755`). Blocks: nothing. Recommendation: keep one Help button in
the bottom bar that opens the help page for the selected Config-R page; Book Settings has none,
so this is a small addition to the shell. Needs the current page key, which `ConfigrPane` does
not expose; track it from the launch key plus a click handler on the nav, or ask config-r for an
`onPageChange`.

**Q9. Experimental page contents.** Tentative decision: list-driven, initially with only the
features that exist (Team Collections). Three of four mockup rows are unmerged features (John,
2026-09-16). Blocks: BL-16738. Recommendation: build the page now as a generic list driven by a
C# registry of experimental features (token, label, description, gating feature name), initially
containing only `team-collections` (and `experimental-source-books` only if its hidden toggle is
ever meant to come back); each feature branch adds one registry entry when it merges.
That removes BL-16738 from the critical path and it can close when the page exists.

**Q10. Bloom Library page gating.** Tentative decision: badge beside the control, as in Book
Settings, for every subscription-gated feature. The card says "the standard subscription
required thing" should be there. Which one: the overlay wrapper the Team Collection tab uses
(`RequiresSubscriptionOverlayWrapper`) or the badge-beside-control pattern Book Settings uses
(`BloomSubscriptionIndicatorIconAndText`)? Blocks: BL-16736 polish only. Recommendation: badge
beside the disabled control, consistent with the Experimental page and Book Settings.

**Q11. Languages: does "Change…" keep the ethnolib chooser in a nested dialog?** Tentative
decision: yes, the ethnolib chooser in a nested React dialog, not a separate WinForms-hosted
one. Today C# opens a `ReactDialog("languageChooserBundle")`. Blocks: BL-16739 mechanics only. Recommendation: mount
`LanguageChooserDialog.tsx` as a nested `BloomDialog` inside the React dialog and drop the C#
`ReactDialog` path and the `settings/changeLanguage` event dance at cutover; the chooser's
result (`languageData.ts`) goes straight into the pending values.

**Q12. Team Collection page: creating a TC from inside the new dialog.** Tentative decision:
converting to a Team Collection becomes a pending change that waits for OK, like every other
change (so Cancel abandons it). Today `TeamCollectionApi.SetCallbackToReopenCollection` forces a
restart and clicks OK on the WinForms dialog. Blocks: BL-16735. The alternative considered was
a "close settings dialog and restart" websocket event applied immediately.

### Nice to settle, blocks nothing

Tentative decision on all three: as recommended.

**Q13.** Dialog size: fixed 900×720 like Book Settings, or resizable? Recommend fixed, same size.
**Q14.** Collection rename: the WinForms path raises `_queueRenameOfCollection` and restarts.
Keep identical. Advanced > Collection Name must be disabled in a TC (John's comment on BL-16737)
with the existing explanation text (`_noRenameTeamCollectionLabel`).
**Q15.** The `version="0.2"` attribute on `.bloomCollection` is written and never read; no
migration is needed for anything in the Aug 2026 design, since every setting maps to an existing
element. Q6's new fields would change that.

## 5. Major steps

Each step is one PR unless noted; the card in bold is the tracker item. Steps 1 to 6 are
independent of each other once step 0 lands, so they can be parallelized across people.

**Step 0. BL-16902 Shell and save pipeline** (see §3). Everything else depends on it.
Deliverable: seven empty localized pages, launch button behind a flag, real GET/POST, extracted
`CollectionSettingsUpdater` with tests, restart plumbing, deep link to a page. Needs Q1, Q2, Q4.

**Step 1. BL-16733 Settings: Front & Back Matter.** Style `ConfigrSelect` (options from the
existing `settings/xmatter` data, honoring the branding-forced pack; description under the
select), QR Codes group (`ConfigrBoolean` + caption `ConfigrInput`, ported from
`AdvancedSettingsPanel.tsx`, plus the new badge preview: C# generates the QR PNG
(`BookStorage.GenerateQrCodeImage`) and assembles the badge as HTML (`BookStorage.UpdateQrCode`),
so a live preview needs a new endpoint, either returning the QR image with TS re-creating the
badge markup and CSS, or rendering the whole badge; neither exists today), Places group (three
`ConfigrInput`s). Page Numbering Style lands here if Q5 goes as recommended. Good first tab: all
existing data, exercises the restart path (xmatter change), and retires `xmatterChooserControl`.

**Step 2. BL-16737 Settings: Advanced.** Program > Automatically Update Bloom (only when
`showAutoUpdate`; note it is a user-level setting, not collection XML), Collection > Collection
Name (disabled in a TC with explanation; rename flows through the extracted updater). Small.

**Step 3. BL-16736 Settings: Bloom Library.** Bookshelf `ConfigrSelect` fed by
`useGetEnterpriseBookshelves` (Contentful), or `DefaultBookshelfControl` inside a
`ConfigrCustomStringInput` if the async option loading fights Config-R. Subscription gating per
Q10. Preserve the expired-bookshelf rule (BL-15056) in the updater, not the UI.

**Step 4. BL-16734 Settings: Subscription.** `SubscriptionSettings` (`subscriptionSettingsTab.tsx`)
inside a `ConfigrStatic`, with `tabMargins` removed. Its own endpoints keep working once they
write to the pending session (§3.2.3); `SubscriptionSettingsEditorApi` subscribes to the
session's cancel instead of the WinForms `DialogCancelled` event. Pro-in-TC refusal and
bookshelf clearing on descriptor change stay in the updater. Verify the "fix invalid branding"
startup path opens this page.

**Step 5. BL-16735 Settings: Team Collection.** `TeamCollectionSettingsPanel` inside a
`ConfigrStatic`, keeping its overlay wrapper and experimental warning. Administrators post to
the session; validation message surfaces per §3.2.4. Page hidden when the feature is off and the
collection is not a TC, and when editing a Bloom Library book, matching
`CollectionSettingsDialog.cs:121-133`. Create-TC flow per Q12.

**Step 6. BL-16739 Settings: Languages.** The large one. Cards for L1, L2, L3 (kept, see §2),
Sign Language: Language row with name and "Change…" (Q11), Default Font row using the existing
`FontSelectComponent`, "Remove" for L3 and SL, "More" per Q3 holding Fonts (Default Font, font
size in tools, line spacing) and Script (Asian breaking, RTL) from `WritingSystem`, plus
Alphabet and Sentence ending punctuation backed by the per-language reader-tool settings (Q6). Reuse `UpdateLanguageSettings` for the apply. Retires `ScriptSettingsDialog`,
`fontScriptSettingsControl`, `singleFontSection`, `bookMakingSettingsControl`. Could be split:
6a language rows and fonts, 6b "More" once Q3 is decided.

**Step 7. BL-16738 Settings: Experimental.** Per Q9: registry-driven list, one row today. Each
row is `ConfigrBoolean` plus the subscription badge, disabled when the tier lacks the feature or
(for Team Collections) when already in a TC, as `AdvancedSettingsPanel.tsx:205-227` does now.

**Step 8. Cutover** (new card). Flip the flag default; make the legacy button, the WinForms
`CollectionSettingsDialog`, `ScriptSettingsDialog`, the four `ReactControl` tab bundles with
their vite entries (`vite.config.mts:568-580`) and the `.entry.tsx` files (two are referenced by
vite, two are orphans already), `DialogBeingEdited`, `WorkspaceApi.HandleShowLegacySettingsDialog`,
and `WorkspaceView.OpenLegacySettingsDialog` go away; point the browser callers of
`common/showSettingsDialog` (`featureStatus.ts`, `localizableMenuItem.tsx`) at
`showCollectionSettingsDialog("subscription")` (it dispatches on its own `document`, so a caller
in an iframe must reach the top frame's copy through the workspace bundle's exports, as
`showBookSettingsDialog` does; the Comic Book ReadMe links are inline `fetch`es
in book content, so they keep an endpoint), and have `CheckForInvalidBranding` send `LaunchDialog`
over the web socket with that page key; mark the unused `CollectionSettingsDialog.*` xlf
strings obsolete (never delete); update the help mapping and the `Settings_dialog_box.htm` family
of help pages; drop `LegacyDpiDialogLauncher` use for this dialog. Add an e2e test that opens the
dialog and changes a setting, which `src/BloomE2E/helpers/collectionSettings.ts` documents as
impossible today ("a test that wanted to measure the Settings dialog itself could not be written").
Update `src/BloomE2E/AUTOMATION-DEBT.md` accordingly. Post test ideas on BL-16271.

**Deferred / separate epics, not part of BL-16271 unless Q2 or Q6 say otherwise:** collection
Appearance defaults and inherit/override (BL-12521, config-r work), Config-r sub-pages
(BL-13273) if Q3 chooses the nested-dialog fallback, moving reader-tool settings (alphabet,
sentence punctuation) into collection settings, per-language keyboard.

## 6. Risks worth naming

- **The pending-session refactor touches the live dialog.** Step 0 changes how the WinForms
  dialog's React tabs store edits. Its unit tests (`CollectionSettingsApiTests`,
  `CollectionSettingsTests`) plus a manual pass over each legacy tab's OK/Cancel/Restart before
  merging keep 6.6 alpha safe.
- **Config-R renders custom controls by identity.** A `control` prop that is not
  `useCallback`-stable remounts on every render (the pattern is used, undocumented, at
  `PageSettingsConfigrPages.tsx:488` and `BookSettingsConfigrPages.tsx:207`). Every reused
  component wrapped for Config-R needs this.
- **Subscription component side channels.** `useSubscriptionInfo` refreshes on a DOM event
  `subscriptionCodeChanged`; inside a `BloomDialog` in the collections tab bundle that still
  works, but the component today assumes it owns the whole tab and is styled by
  `enterpriseSettings.less`; expect layout tweaks.
- **Search.** Leave it off; it indexes labels and Config-R's README says it is not
  production-ready. Turning it on later is one prop.
- **Target branch.** Master's `AGENTS.md` currently says new work branches from `Version6.5`;
  BL-16271 is on the 6.6 board and this plan was written on a branch from master at the
  requester's direction. The shell PR should confirm its base before opening.
