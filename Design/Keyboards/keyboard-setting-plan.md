# Per-language keyboard setting: generalize the KeymanWeb POC + holistic OS keyboard support

## Context

The `Keyman` branch has a POC (commit af6f479a2) that hard-codes the Thai `thai_kedmanee` KeymanWeb keyboard into the edit view, loaded from the Keyman CDN. We now generalize this into a real per-language **keyboard setting**, designed holistically around one key fact: **Keyman for Windows (and any Windows input method) is a TSF input processor that already works underneath WebView2** — it's the ambient condition, not a feature to build. So the setting answers: *"Should Bloom itself supply a keyboard for this language, and if so, which one?"*

Decisions made with John:
- **Default = Automatic cascade, resolved per machine at edit time**: if this machine's OS has an input method for the field's language (includes installed Keyman keyboards) → activate it, never attach KeymanWeb. Otherwise → attach the collection's cached KMW fallback keyboard (default = top suggestion from the Keyman search API, fetched/cached first time online; silent retry when offline).
- **Active OS switching in v1** (FieldWorks-style): focusing a field switches the Windows input method to match its language; focusing English switches back. Browser → C# focus notification, libpalaso `KeyboardController`.
- **Chooser = Automatic + full list**: per-language dropdown offers Automatic (showing what it resolves to on this machine), then installed input methods, then Keyman-cloud keyboards (ordered by popularity). Picking an explicit item pins it. Conflicts are resolved at settings time; edit view just honors the setting.
- **Setting lives per-language in collection settings** (on `WritingSystem`, next to `FontName`, in `.bloomCollection`), syncing via Team Collections. Per-machine resolution of Automatic makes that safe (Keyman-equipped teammate → OS wins; bare machine → KMW kicks in).
- **Offline-first in v1**: vendor the KMW 18 engine with Bloom; download the chosen/suggested keyboard's `.js` + OSK font into the collection folder so it syncs and typing never needs the network.

## Data model & serialization

- `WritingSystem.Keyboard` (string, new field; skip for sign language):
  - `""` / absent → Automatic (default)
  - `"system:<libpalaso keyboard Id>"` → pinned installed input method
  - `"kmw:<keyboardId>@<bcp47>"` → pinned KeymanWeb keyboard
- `WritingSystem.CachedKmwFallbackKeyboard` (string): top search-API result, used only by Automatic; empty until fetched.
- Serialize in both existing forms following the `FontName` pattern in `SaveToXElementInternal`/`ReadFromXmlInternal`: legacy `Language{n}Keyboard` / `Language{n}CachedKmwKeyboard` and unnumbered form inside `<Language>`. Copy in `Clone()`.
- New value type `src/BloomExe/Keyboarding/KeyboardSetting.cs`: `Parse`/`ToString`, `Kind {Automatic, System, KeymanWeb}`.

## Resolution cascade (C#, per machine, cached per lang per session)

For focused field lang X (match language subtag, ignoring region/script):
1. `system:<id>` → `KeyboardController.Instance.TryGetKeyboard(id)` → `Activate()`; no KMW. Not installed here → fall through to Automatic.
2. `kmw:<id>@<tag>` → reply "attach KMW"; ensure files cached (background retry when online).
3. Automatic → best matching `AvailableKeyboards` entry (exact locale > language-only) → activate, no KMW. Else `CachedKmwFallbackKeyboard` (background-fetch if empty & online) → attach KMW. Else `ActivateDefaultKeyboard()`, no KMW.
4. Langs with no `WritingSystem` (`z`, `*`, source-bubble langs) and all non-KMW fields → `ActivateDefaultKeyboard()` (switch back for English fields).

Settings changes mark `ChangeThatRequiresRestart()` (mirroring fonts), so session caching is safe.

## Keyman API facts (verified live)

- Chooser list: `https://api.keyman.com/search/2.0?q=l:id:<tag>` → `keyboards[]` with `id`, `name`, `match.finalWeight`, downloads. No official "default" flag — order by `finalWeight`; top = de-facto suggestion.
- Download metadata: `https://api.keyman.com/cloud/4.0/keyboards/{id}/{lang}?languageidtype=bcp47` → `options.keyboardBaseUri`, `keyboard.filename`, per-language `font`/`oskFont`.
- No npm package for the KMW engine — vendor MIT-licensed release artifacts (18.0.x).

## Ordered work items

### 0. De-risking spike FIRST (throwaway): OS switching under WebView2
libpalaso's `WindowsKeyboardSwitchingAdapter` uses `ITfInputProcessorProfileMgr.ActivateProfile(..., TF_IPPMF_FORPROCESS)` — process-scoped, while WebView2 input lives in separate `msedgewebview2.exe` processes. **Must prove activation affects typing in a bloom-editable before building on it.**
- Temp endpoint in `src/BloomExe/web/controllers/KeyboardingConfigApi.cs` (`handleOnUiThread: true`): `KeyboardController.Initialize()` once, then `TryGetKeyboard(id).Activate()`. Test via run-bloom with a Windows Thai layout + a Keyman keyboard installed.
- If it fails, evaluate in order: `WM_INPUTLANGCHANGEREQUEST` to the focused WebView2 HWND; calling `ActivateProfile` ourselves with `TF_IPPMF_FORSESSION`; escalate to libpalaso team (their master has fresh tracing in this exact path). Worst case v1 ships KMW + passive OS behavior; setting shape unchanged.
- Also verify `KeyboardController.Initialize()` doesn't disturb the existing `useLongpress` check, and measure init cost.

### 1. Model + serialization
`src/BloomExe/Collection/WritingSystem.cs`, new `src/BloomExe/Keyboarding/KeyboardSetting.cs`; tests in `src/BloomTests/Collection/CollectionSettingsTests.cs` (round-trip both XML forms, defaults, Parse cases).

### 2. Startup init + enumeration
`src/BloomExe/Program.cs`: `KeyboardController.Initialize()` as a low-priority startup action on the UI thread (try/catch + NonFatalProblem), `Shutdown()` beside `Sldr.Cleanup()`. New `src/BloomExe/Keyboarding/OsKeyboards.cs`: `GetInstalledKeyboardsForLanguage(tag)`, `FindBestForLanguage(tag)`, `TryActivate(id)`, `ActivateDefault()` — UI-thread-only.

### 3. Keyman cloud client + collection cache + TC sync
New `src/BloomExe/Keyboarding/KeymanCloudClient.cs` (5s-timeout HttpClient; JSON parsing split into pure methods tested with canned fixtures) and `CollectionKeyboardCache.cs` (`<collection>/Keyboards/` with `<id>.js`, `<id>.json` manifest, `fonts/`; atomic temp+rename writes; `GetJsUrl` via `ToLocalhost()`).
Team Collections: add `"Keyboards"` alongside `"Allowed Words"`/`"Sample Texts"` in `TeamCollection.cs` (~1100–1147) and `FolderTeamCollection.cs` (~421, ~586). Extend existing TC sync tests.

### 4. Edit-time endpoint + OS switching
New `src/BloomExe/Keyboarding/KeyboardResolver.cs` (cascade + `ConcurrentDictionary` session cache + background fallback fetch that saves `CachedKmwFallbackKeyboard` — marshal the save; don't clobber an open settings dialog).
`KeyboardingConfigApi.cs`: `POST keyboarding/fieldFocused {lang}` with `handleOnUiThread: true` → activation side-effect (only when `ActiveKeyboard` differs) + reply `{useKmw, keyboardId, languageTag, keyboardFileUrl, fontFamily?, fontUrls?, oskFontFamily?, oskFontUrls?}`. Inject `CollectionSettings` via the existing autofac registration (`ProjectContext.cs` 175/420). One HTTP call per focus change, never per keystroke.

### 5. Vendor engine + generalize JS integration
New `src/BloomBrowserUI/keymanweb/` (engine js + OSK resources + osk font + LICENSE + README with version/update procedure) — vite's `viteStaticCopy` ships it to `output/browser` automatically; verify `scripts/dev.mjs` watcher behavior and note that the developer must run one build to seed it (do NOT run `yarn build` as agent).
Rewrite `src/BloomBrowserUI/bookEdit/js/keymanWebIntegration.ts`: engine from `/bloom/keymanweb/keymanweb.js`, `keyman.init({attachType:"manual", root/resources/fonts: "/bloom/keymanweb/"})` (keep the POC's hard-won comments); on focusin post `keyboarding/fieldFocused`; if `useKmw` → `addKeyboards({id, filename: keyboardFileUrl, ...})` (local stub, no cloud), keep the POC's `HasLoaded` poll, `attachToControl` + `setKeyboardForControl` (WeakSet), `setActiveKeyboard` + `osk.show`; else `osk.hide()` and do nothing (C# already switched the OS keyboard). Client cache `Map<lang, info>` to skip re-registration; still post every focus so C# can switch OS keyboards. Skip `z`/`*`/empty langs. Keep the existing focusin hook in `bloomEditing.ts` (~975–986) and its `Cleanup()` scrub.

### 6. Settings UI (Book Making tab)
`CollectionSettingsApi.cs`: `GET settings/keyboardsForLanguage?languageNumber=` → `{current, automaticResolvesTo: {kind, displayName}, installed: [{id,name}], cloud: [{id,name,downloads}]}` (cloud proxied through C#, empty offline); `POST settings/setKeyboardForLanguage` using the pending pattern (`PendingKeyboardSelections` array in `CollectionSettingsDialog.cs` init ~96–101, commit in `UpdateLanguageSettings` — extend signature + its tests; `ChangeThatRequiresRestart()` on change). On OK-commit of a `kmw:` pin, kick `EnsureDownloaded` in background.
New `src/BloomBrowserUI/react_components/keyboardSection.tsx` (MUI Select: Automatic w/ dynamic "resolves to" secondary text, installed group, cloud group), rendered per language via `fontScriptSettingsControl.tsx`/`singleFontSection.tsx`; extend `currentFontData` to carry `languageTag` + `keyboard`.
**Localizable strings → follow `.github/skills/xlf-strings/SKILL.md`** (only `DistFiles/localization/en/`; IDs like `CollectionSettingsDialog.BookMakingTab.KeyboardFor` beside `...DefaultFontFor` in `Bloom.xlf`; ask John which xlf file if priority is unclear).

### 7. C#-side scrub (data-div/xmatter) — closes the POC's flagged gap
`src/BloomExe/Book/BookData.cs`: add `"keymanweb-font"` to `_classesNotToCopy` (~1722) AND `_classesToRemoveIfAbsent` (~1738, heals POC-era pollution); `"inputmode"` to `_attributesNotToCopy` (~1685) AND `_attributesToRemoveIfAbsent` (~1715); value-sensitive skip of `dir="ltr"` in `GetAttributesToSave` (~1770; Bloom only ever writes `dir="rtl"`). Tests in `BookDataTests` (polluted xmatter → clean data-div + clean re-emitted page; healing case). Keep the JS scrub unchanged.

### 8. Longpress interplay + polish
When KMW attaches to an editable, disable longpress on that element (`activateLongPressFor`, `bloomEditing.ts` ~1812–1863); OS-switched fields keep longpress (the global `IsFormUsingInputProcessor` check already covers desktop Keyman). L4+ languages: resolver handles them (Automatic-only; no settings row in v1). Sign language: nothing.

## Verification

- C#: `dotnet test` (never `--no-build`) — `CollectionSettingsTests`, new Keyboarding tests, `BookDataTests`, TC sync tests.
- JS: `yarn lint`, `yarn typecheck`, vitest for keymanWebIntegration (mock bloomApi). **No `yarn build`.**
- Manual via run-bloom skill:
  1. Thai collection, no Thai OS keyboard → Automatic fetches+caches fallback, OSK appears, typing remaps; English fields unaffected, OSK hides.
  2. Install Windows Thai Kedmanee → Automatic now activates OS keyboard on focus, no KMW; English field switches back.
  3. Pin a cloud keyboard while an OS keyboard exists → KMW wins (setting honored).
  4. Offline with a new language → no errors; suggestion fetched silently next time online.
  5. Team collection: `Keyboards/` folder syncs both directions.
  6. Save a book with xmatter fields → saved HTML free of `keymanweb-font`/`inputmode`/`dir="ltr"`.

## Risks (ranked)

1. **OS activation under WebView2** — spiked first (item 0) with named fallbacks; scope decision point if it fails.
2. TSF/Keyman TIP quirks (TIP vs HKL profiles, IME conversion state for CJK) — cover in spike.
3. First-focus latency (engine + keyboard load) — mitigate by prefetching `fieldFocused` for L1–L3 at page load.
4. ckeditor/undo interaction with KMW in contenteditables — watch source bubbles & canvas text during manual verification.
5. Dev-mode serving of vendored engine (static copy is build-only) — verify `dev.mjs`, document.
6. Background save of `CachedKmwFallbackKeyboard` racing an open settings dialog — only write that one field, marshal to UI thread.
