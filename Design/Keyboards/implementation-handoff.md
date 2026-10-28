# Handoff: Implement the per-language keyboard setting (branch `Keyman`)

**For:** a fresh agent session (Opus) implementing this feature.
**The plan (authoritative):** `Design/Keyboards/keyboard-setting-plan.md` — read it first; this document only adds context the plan doesn't carry.

## Where things stand

- Branch has two commits beyond master relevant here: `af6f479a2` (the KeymanWeb POC — hard-coded Thai `thai_kedmanee`, CDN-loaded engine, all browser-side in `src/BloomBrowserUI/bookEdit/js/keymanWebIntegration.ts` + hooks in `bloomEditing.ts`) and `f8376794a` (unrelated skill helper).
- **No implementation of the plan has started.** The planning session ended right after the plan was written.
- The design was worked out interactively with John and he made explicit decisions. **Do not relitigate them** (see "Decisions and their rationale" below). He reviewed the plan and said it is good.

## Non-negotiable first step: the spike (plan item 0)

Do the WebView2 OS-keyboard-switching spike **before** building anything else. libpalaso's `WindowsKeyboardSwitchingAdapter` activates TSF profiles with `TF_IPPMF_FORPROCESS` (process-scoped), but WebView2 keyboard input is consumed in separate `msedgewebview2.exe` processes — so activation may not affect where the user actually types. This is a go/no-go gate for the "active OS switching in v1" decision. Fallbacks to try, in order, are in the plan. If all fail, go back to John with findings before descoping; the stored setting shape does not change either way.

## Decisions and their rationale (so you can defend them, not re-ask them)

1. **The setting is a resolution policy, not a keyboard picker.** Default "Automatic" = per-machine cascade: OS input method for the language if this machine has one (this *includes* installed Keyman-for-Windows keyboards, which register as ordinary TSF TIPs — no Keyman-specific detection code needed), else the collection's cached KeymanWeb fallback (top suggestion from the Keyman search API). Rationale: Keyman-for-Windows already works underneath WebView2 with zero cooperation from Bloom, so "do nothing" IS the installed-Keyman path; KMW exists for machines with nothing installed.
2. **Conflicts are handled at settings time, not edit time.** The edit view honors whatever the setting resolves to. John was explicit: "The Edit view should do whatever the selector in the settings has chosen."
3. **Active switching in v1** (FieldWorks-style: focusing a Thai field activates the installed Thai keyboard; focusing English switches back). John chose this over passive knowingly, subject to the spike.
4. **Setting lives per-language on `WritingSystem` in `.bloomCollection`** (syncs via Team Collections). The per-machine resolution of Automatic is what makes syncing safe (Keyman-equipped teammate → OS wins; bare machine → KMW kicks in). John himself proposed "OS keyboard first, default KeymanWeb keyboard second."
5. **Offline-first is a hard requirement.** John: "we need to have a strong offline story here where everything keeps working if you're offline... we have already cached everything that we need." Engine vendored into Bloom; keyboard `.js` + OSK font downloaded into `<collection>/Keyboards/` at selection/suggestion time.
6. **Smart default on new language:** if the OS lacks a keyboard for it and we're online, default to the Keyman API's top suggestion; if offline, silently retry later. If an OS keyboard exists, Automatic surfaces/pre-selects it.
7. **Long-press is disabled per-field when KMW attaches** — John probed this twice; the accepted rationale: (a) long-press alternates are variants of the physical key, which is meaningless under remapping; (b) both libraries intercept the same key events; (c) it extends the existing BL-1071 policy (window-level disable when a system input processor is active) to field granularity. Caveat given to John: the event conflict is reasoned, not empirically demonstrated with KMW — if they demonstrably coexist, this is a one-line decision to revisit; the nonsense-alternates argument holds regardless.

## Verified facts — do NOT re-research these

- **Keyman search API** (chooser list): `https://api.keyman.com/search/2.0?q=l:id:<bcp47>` → `keyboards[]` with `id`, `name`, `match.finalWeight`, download counts. There is **no** official "default keyboard for language" flag; order by `finalWeight`, top = de-facto suggestion. Verified live 2026-07-09.
- **Keyman download metadata**: `https://api.keyman.com/cloud/4.0/keyboards/{id}/{lang}?languageidtype=bcp47` → `options.keyboardBaseUri` (`https://s.keyman.com/keyboard/`), `options.fontBaseUri`, `keyboard.filename`, per-language `font`/`oskFont`. Verified live.
- **No npm package for the KeymanWeb engine** (`@keymanapp/keymanweb` is a 404; registry search found nothing). Vendor the 18.0.x release artifacts (MIT license). The POC pins `18.0.249` and its comments document why `root`/`resources`/`fonts` must all be set in `keyman.init` — keep those comments.
- **KMW API facts**: `addKeyboards({id, filename, languages...})` object form registers a local stub (no cloud); `attachToControl` + `setKeyboardForControl(el, id, lang)` give per-field keyboards; `setActiveKeyboard` is global. The POC's `HasLoaded` polling exists because `setActiveKeyboard` rejects before the lazily-fetched keyboard JS arrives ("Cannot read properties of null (reading 'metadata')") — keep that workaround.
- **libpalaso** (reflected from the actual `SIL.Windows.Forms.Keyboarding` 18.0.0-beta0014 DLL in `output/Debug/AnyCPU`): `KeyboardController.Initialize()` (Bloom never calls it today; the existing `IsFormUsingInputProcessor` in `KeyboardingConfigApi.cs` works without it), `Instance.AvailableKeyboards` (`IKeyboardDefinition {Id, Name, Locale, Layout, IsAvailable, Activate()}`), `TryGetKeyboard(id)`, `ActivateDefaultKeyboard()`, `ActiveKeyboard`. Activation path ends in `ITfInputProcessorProfileMgr.ActivateProfile(..., TF_IPPMF_FORPROCESS)` — the spike risk. libpalaso master (2025 commits) has fresh trace logging in exactly this switching path, so escalating to that team is viable.
- **Team Collections** sync only `RootLevelCollectionFilesIn` (`.bloomCollection`, `customCollectionStyles.css`, `configuration.txt`, `ReaderTools*.json`) plus hard-coded folders `"Allowed Words"` and `"Sample Texts"` — `TeamCollection.cs:~1100-1147`, `FolderTeamCollection.cs:~421-422, ~586-587`. Adding `"Keyboards"` requires touching all three spots.
- **BloomServer** serves arbitrary disk files via `/bloom/<path>` (`LocalHostPathToFilePath`, ~line 1141); use `path.ToLocalhost()`. Vite's `viteStaticCopy` (vite.config.mts:626-648) copies non-ts/pug/less files from `src/BloomBrowserUI` to `output/browser`, so a vendored `src/BloomBrowserUI/keymanweb/` folder ships on build — but static copy is **build-only**; check `scripts/dev.mjs` watcher behavior and ask John to run one build to seed `output/browser/keymanweb/` (the agent must never run `yarn build`).
- **Settings plumbing pattern to mirror**: `CollectionSettingsApi.UpdatePendingFontName` → `CollectionSettingsDialog.PendingFontSelections` (line ~57) → committed in `UpdateLanguageSettings` (~line 591) → `ChangeThatRequiresRestart()`. Keyboard changes should trigger restart too, which makes per-session resolution caching safe.
- **Data-div scrub hooks** (`BookData.cs`): `_attributesNotToCopy` ~1685, `_attributesToRemoveIfAbsent` ~1715, `_classesNotToCopy` ~1722, `_classesToRemoveIfAbsent` ~1738, `GetAttributesToSave` ~1770. `dir` needs a value-sensitive skip (only strip `dir="ltr"`; Bloom itself only writes `dir="rtl"` — same reasoning as the JS scrub comment at `bloomEditing.ts:~182`).

## Open questions to raise with John during implementation

- **Which XLF file** the new settings strings belong in (likely `Bloom.xlf` next to `CollectionSettingsDialog.BookMakingTab.DefaultFontFor`, but the xlf-strings skill says ask about priority file).
- Whether the long-press/KMW conflict reproduces empirically (cheap to test once KMW attach is working; see decision 7).
- L4+ languages get Automatic-only behavior with no settings row in v1 — confirm that's acceptable when the UI lands.

## Working-style notes for this user (John)

- He explicitly wants to be **talked with, not deferred to** — give genuine opinions, push back, and engage conversationally with his answers rather than just recording them.
- Anything posted under his account (PR comments etc.) must start with `[Claude <model name>]`.
- Finished work lands in **"Personal Review"** on his board — never auto-promote to Peer Review (see the `personal-board` skill).
- Never `yarn build` (a --watch build may be running); never npm; `dotnet test` never with `--no-build`; only edit localization under `DistFiles/localization/en/`.

## Suggested skills

- `.github/skills/xlf-strings/SKILL.md` (repo skill) — **mandatory** when adding the settings-UI strings (plan item 6).
- `run-bloom` — for the spike (item 0) and all manual verification (plan's Verification section).
- `verify` — before committing each substantive slice.
- `preflight` — once implementation is complete, to push, open the draft PR, and run the bot gauntlet.
- `personal-board` — move the card to "Personal Review" when done (never Peer Review).
