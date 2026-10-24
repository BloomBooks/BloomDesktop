// KeymanWeb (KMW) per-language keyboard integration. Implements plan item 5 of
// Design/Keyboards/keyboard-setting-plan.md: for every focused bloom-editable we
// ask the C# side (which does not exist yet — see plan item 4 — but whose
// contract is fixed) what this machine/collection wants for that field's
// language, and either let KMW take over typing for the field or get out of the
// way (C# has already switched the OS input method itself in that case).
//
// Supersedes the hard-coded Thai POC (commit af6f479a2).

import { get, postBoolean, postJsonAsync } from "../../utils/bloomApi";
import { setKmwAttached } from "../longPressShared";

// The engine is vendored (see keymanweb/README.md), not CDN-loaded — this is
// an offline app. BloomServer serves arbitrary disk files under /bloom/<path>,
// which is where the vendored src/BloomBrowserUI/keymanweb/ folder lands. This
// is the page-relative path, fine for the browser's own <script src> fetch.
const kKeymanEngineFilesPath = "/bloom/keymanweb/";

// Bloom's own pseudo-language markers (source-bubble/xmatter placeholders, "any
// language" fields). There is no writing system to resolve for these, so we
// never even ask the server about them.
const kSkippedLangs = new Set(["z", "*", ""]);

// The shape POST keyboarding/fieldFocused replies with (plan item 4's fixed
// contract; the C# endpoint doesn't exist yet, so we code against this).
export interface FieldKeyboardInfo {
    useKmw: boolean;
    keyboardId: string;
    languageTag: string;
    keyboardFileUrl: string;
    fontFamily?: string;
    fontUrls?: string[];
    oskFontFamily?: string;
    oskFontUrls?: string[];
}

// Track attached elements in JS, NOT via a DOM attribute/class — the page DOM
// gets saved into the book, so we must not pollute it.
const attachedEditables = new WeakSet<HTMLElement>();

// Caches the keyboard we've actually registered with KMW for each language, so
// refocusing a field of the same language (with the same server-chosen
// keyboard) skips the lazy addKeyboards/HasLoaded dance. We still POST
// fieldFocused on every focus regardless of this cache: see
// attachKeymanWebIfNeeded — C# needs the notification to switch the OS
// keyboard even when KMW itself has nothing new to do.
const registeredKeyboardByLang = new Map<string, FieldKeyboardInfo>();

// The most recent fieldFocused response for each editable (including useKmw:
// false), so a control mounted after focus — e.g. the React keyboard indicator,
// which React 18 mounts asynchronously — can read the current state without
// re-POSTing. We also dispatch kFieldKeyboardInfoEvent on the editable so an
// already-mounted control updates when a (possibly later) response arrives.
const keyboardInfoByEditable = new WeakMap<HTMLElement, FieldKeyboardInfo>();

// Event dispatched on an editable when its fieldFocused info is (re)resolved.
// The detail is the FieldKeyboardInfo.
export const kFieldKeyboardInfoEvent = "bloom-fieldKeyboardInfo";

// The last-known keyboard info for an editable, or undefined if none has been
// resolved yet.
export function getKeyboardInfoFor(
    editable: HTMLElement,
): FieldKeyboardInfo | undefined {
    return keyboardInfoByEditable.get(editable);
}

// The user's desired on-screen-keyboard visibility. For a KMW field we show the
// OSK automatically by default, but once the user closes it we must NOT keep
// popping it back up every time they focus another KMW field. So closing the OSK
// turns this off (until the user asks for it again via the keyboard indicator),
// and we persist it as a user preference so the choice survives across sessions.
const kOskVisibleApi = "keyboarding/oskVisible";
let oskDesiredVisible = true;
let oskDesiredVisibleLoaded = false;
let oskHideListenerRegistered = false;

// True while an OSK "hide" event is expected to come from our OWN hideOsk()
// (because focus moved to a non-KMW field) rather than from the user closing the
// OSK. We need this because on desktop KeymanWeb refuses a truly "programmatic"
// hide (its mayHide() rejects a non-user hide on desktop), so hideOsk() has to
// call the public hide(), which fires the "hide" event with HiddenByUser:true —
// exactly like a real user close. Without this flag, moving from a Thai field to
// an English one and back would look like the user had closed the OSK, and we'd
// stop auto-showing it (BL: reported "Thai keyboard doesn't come back").
let oskHideIsProgrammatic = false;

// Load the persisted preference once (served by keyboarding/oskVisible). Any
// error — e.g. an older host build without the endpoint — just leaves the
// default (visible), so field focus never blocks on this.
function ensureOskDesiredVisibleLoaded(): Promise<void> {
    if (oskDesiredVisibleLoaded) return Promise.resolve();
    oskDesiredVisibleLoaded = true;
    return new Promise<void>((resolve) => {
        get(
            kOskVisibleApi,
            (result) => {
                // Treat anything but an explicit false as "visible".
                oskDesiredVisible = result.data !== false;
                resolve();
            },
            // Endpoint not present yet: keep the default and stay quiet (the
            // error callback prevents an error toast).
            () => resolve(),
        );
    });
}

// Record and persist the user's desired OSK visibility. Fire-and-forget: the
// POST matches the same contract-first pattern as keyboarding/fieldFocused.
function setOskDesiredVisible(visible: boolean): void {
    oskDesiredVisible = visible;
    oskDesiredVisibleLoaded = true; // our value now wins over any stale load
    postBoolean(kOskVisibleApi, visible);
}

// KMW fires an OSK "hide" event whenever the OSK hides; its argument carries
// HiddenByUser, which is true only when the user clicked the OSK's own close
// button (as opposed to our programmatic hides on non-KMW fields). Registered
// once, after the engine has loaded.
function registerOskHideListenerOnce(keyman: any): void {
    if (oskHideListenerRegistered) return;
    oskHideListenerRegistered = true;
    keyman.osk?.addEventListener?.("hide", (obj: any) => {
        const wasProgrammatic = oskHideIsProgrammatic;
        oskHideIsProgrammatic = false;
        // Only a genuine user close (the OSK's own close button) should stop us
        // auto-showing it; our own hideOsk() on non-KMW fields must not.
        if (obj?.HiddenByUser && !wasProgrammatic) {
            setOskDesiredVisible(false);
        }
    });
}

// Re-show the KeymanWeb on-screen keyboard (for the keyboard indicator's click
// handler) and remember that the user now wants it visible. Guarded (?.) because
// the OSK API surface can vary across engine builds and the engine may not be
// loaded at all.
export function showOsk(): void {
    setOskDesiredVisible(true);
    (window as any).keyman?.osk?.show?.(true);
}

function recordKeyboardInfo(
    editable: HTMLElement,
    info: FieldKeyboardInfo,
): void {
    keyboardInfoByEditable.set(editable, info);
    editable.dispatchEvent(
        new CustomEvent<FieldKeyboardInfo>(kFieldKeyboardInfoEvent, {
            detail: info,
        }),
    );
}

let keymanSetupPromise: Promise<any> | undefined;

// Injects the KeymanWeb engine <script> the first time it is needed, then
// initializes it in "manual" attach mode (so we choose which controls it
// touches). Subsequent calls reuse the same promise, so the engine is loaded
// and initialized exactly once. Must only be called once we know a field
// actually needs KMW (useKmw: true) — loading the engine unconditionally would
// hurt first-focus latency on every field, including plain English ones.
function ensureEngineLoaded(): Promise<any> {
    if (!keymanSetupPromise) {
        keymanSetupPromise = new Promise<void>((resolve, reject) => {
            const script = document.createElement("script");
            script.src = `${kKeymanEngineFilesPath}keymanweb.js`;
            script.onload = () => resolve();
            script.onerror = () =>
                reject(new Error("Failed to load keymanweb.js"));
            document.head.appendChild(script);
        })
            .then(() => {
                // Point ALL resource paths at our vendored folder, using
                // FULLY-QUALIFIED URLs. This is load-bearing: KMW's internal
                // fixPath() treats a page-relative value (leading "/") as
                // relative to where it *thinks* keymanweb.js loaded from
                // (its inferred "sourcePath"). Because we inject the <script>
                // dynamically, it mis-infers that base and produces doubled
                // garbage like ".../keymanweb/bloom/keymanweb//osk//keymanweb-osk.ttf"
                // (a real 404 we hit). fixPath returns any http(s):-prefixed
                // value verbatim, so an absolute URL sidesteps the mangling.
                //
                // Trailing slash matters and DIFFERS per key: `root` is used as
                // base + a leading-slash-stripped relative path, so it needs the
                // trailing slash; `resources` and `fonts` are used as
                // `${value}/osk/<file>` (the engine supplies the slash), so they
                // must NOT end in one — otherwise we get `//osk//`.
                // attachType "manual" = we choose which controls it touches.
                const base = `${window.location.origin}${kKeymanEngineFilesPath}`;
                return (window as any).keyman.init({
                    attachType: "manual",
                    root: base, // keeps its trailing slash
                    resources: base.replace(/\/$/, ""),
                    fonts: base.replace(/\/$/, ""),
                });
            })
            .then(() => (window as any).keyman);
    }
    return keymanSetupPromise;
}

// addKeyboards() only registers a keyboard STUB; the keyboard's actual code
// (its .js, served from the collection's local cache) downloads lazily and is
// NOT present immediately after. Binding or activating the keyboard before
// that code has loaded makes KeymanWeb throw — verified empirically against
// the live engine: on a fresh load setKeyboardForControl throws "Cannot read
// properties of null (reading 'metadata')" and setActiveKeyboard can reject
// with "...keyboard script...may contain an error", because the loaded
// Keyboard object is still null. So we trigger the download and wait for the
// engine to mark the keyboard loaded before letting callers bind/activate it.
async function waitForKeyboardLoaded(
    keyman: any,
    info: FieldKeyboardInfo,
): Promise<void> {
    // KeymanWeb prefixes the id with "Keyboard_" for the entry it exposes in
    // getKeyboards(); that is the name we match on when checking load state.
    const internalName = `Keyboard_${info.keyboardId}`;
    const isLoaded = () =>
        (keyman.getKeyboards() || []).some(
            (k: any) => k.InternalName === internalName && k.HasLoaded,
        );
    // Activating the keyboard is what triggers its lazy code download. This
    // call can itself reject while the code is not present yet (exactly the
    // race we are guarding against), so ignore that rejection here; the poll
    // below on the engine's own HasLoaded flag is our real completion signal.
    keyman.setActiveKeyboard(info.keyboardId, info.languageTag).catch(() => {});
    // Bounded poll (100ms x 50 = 5s). Empirically the keyboard loads in well
    // under a second; we fail fast rather than silently continuing with an
    // unusable keyboard if the download never completes.
    for (let attempt = 0; attempt < 50; attempt++) {
        if (isLoaded()) return;
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(
        `KeymanWeb keyboard ${info.keyboardId} did not finish loading within 5s`,
    );
}

// Registers (if not already registered for this language) and activates the
// keyboard the server chose for `lang` on `editable`, then shows the OSK.
async function attachKmwKeyboard(
    editable: HTMLElement,
    lang: string,
    info: FieldKeyboardInfo,
): Promise<void> {
    const keyman = await ensureEngineLoaded();
    registerOskHideListenerOnce(keyman);
    await ensureOskDesiredVisibleLoaded();

    const alreadyRegistered =
        registeredKeyboardByLang.get(lang)?.keyboardId === info.keyboardId;
    if (!alreadyRegistered) {
        // Register the stub for the EXACT keyboard the server chose, using a
        // local file (object form = local stub, no cloud lookup) — the
        // collection has already cached this keyboard's .js (plan item 3).
        //
        // The engine's validateForCustomKeyboard REQUIRES a top-level keyboard
        // name and a language region, not just ids (verified empirically:
        // omitting either makes addKeyboards report "To use a custom keyboard,
        // you must specify file name, keyboard id, keyboard name, language,
        // language code, and region"). Neither value is shown anywhere in
        // Bloom (they feed KMW's own picker UI, which we don't use), so the
        // keyboard id and a placeholder region satisfy it.
        //
        // font/oskFont property names verified against the vendored engine
        // itself (src/BloomBrowserUI/keymanweb/keymanweb.js is a minified
        // bundle, no separate typings ship with it): its internal
        // "internalizeFont" helper reads `family` and `filename || source`
        // off exactly these two language-spec properties, e.g.
        //   function ps(g,n){if(g)return{family:g.family,path:n,files:g.filename||g.source}}
        // called as ps(i.languages.font, ...) / ps(i.languages.oskFont, ...).
        // We pass `source` (an array of font URLs) rather than `filename`
        // since fontUrls/oskFontUrls are arrays.
        const addResults = await keyman.addKeyboards({
            id: info.keyboardId,
            name: info.keyboardId,
            filename: info.keyboardFileUrl,
            languages: [
                {
                    id: info.languageTag,
                    name: info.languageTag,
                    region: "World",
                    ...(info.fontFamily && {
                        font: {
                            family: info.fontFamily,
                            source: info.fontUrls,
                        },
                    }),
                    ...(info.oskFontFamily && {
                        oskFont: {
                            family: info.oskFontFamily,
                            source: info.oskFontUrls,
                        },
                    }),
                },
            ],
        });
        // addKeyboards does NOT reject on a bad stub; it resolves with an
        // array whose entries carry an `error`. Surface that loudly — a
        // silently unregistered keyboard was exactly how this integration
        // originally failed.
        const failed = addResults.find((r: any) => r.error);
        if (failed) {
            throw new Error(
                `KeymanWeb rejected keyboard ${info.keyboardId}: ${failed.error.message ?? failed.error}`,
            );
        }
        await waitForKeyboardLoaded(keyman, info);
        registeredKeyboardByLang.set(lang, info);
    }

    // Attach + bind each control only once; doing it repeatedly is unnecessary.
    if (!attachedEditables.has(editable)) {
        attachedEditables.add(editable);
        // Disables long-press on this element (see longPressShared.ts for why).
        setKmwAttached(editable);
        keyman.attachToControl(editable);
        // Future focus events on this control select the keyboard automatically:
        keyman.setKeyboardForControl(
            editable,
            info.keyboardId,
            info.languageTag,
        );
    }

    // Activate the keyboard and show the OSK on EVERY KMW focus, not just the
    // first attach. Otherwise, once we hide the OSK on a non-KMW field, coming
    // back to an already-attached field would leave the OSK hidden.
    await keyman.setActiveKeyboard(info.keyboardId, info.languageTag);
    // On desktop, activating a keyboard does not necessarily reveal the
    // floating on-screen keyboard, so ask for it explicitly — but only if the
    // user both wants it visible (they haven't closed it; see
    // oskDesiredVisible) and is still in this field: the first registration
    // awaits a keyboard download, and by the time it finishes focus may have
    // moved to a non-KMW field whose handler already ran (and hid the OSK).
    // Guarded (?.) because the OSK API surface can vary across engine builds and
    // we don't want a missing method to break field focusing.
    if (
        oskDesiredVisible &&
        (document.activeElement === editable ||
            editable.contains(document.activeElement))
    ) {
        keyman.osk?.show?.(true);
    }
}

// Hide the on-screen keyboard when focus lands somewhere KMW isn't handling.
// Only meaningful once the engine has loaded (some KMW field was focused
// earlier this session); before that window.keyman is undefined and this is a
// no-op. The getActiveKeyboard() check matters: the engine's osk.hide()
// throws ("Cannot read properties of undefined (reading 'keyboard')",
// verified against the vendored 18.0 engine) if no keyboard has ever been
// activated — and in that state there is nothing to hide anyway.
function hideOsk(): void {
    const keyman = (window as any).keyman;
    if (keyman?.getActiveKeyboard?.()) {
        // Flag this as OUR hide so the "hide" listener doesn't mistake it for a
        // user close (see oskHideIsProgrammatic). hide() on desktop fires the
        // event synchronously, but guard with a timeout in case a build fires it
        // async or the OSK was already hidden (no event at all), so a later
        // genuine user close is still honored.
        oskHideIsProgrammatic = true;
        keyman.osk?.hide?.();
        setTimeout(() => {
            oskHideIsProgrammatic = false;
        }, 500);
    }
}

function normalizeLang(rawLang: string | null): string {
    return (rawLang ?? "").trim();
}

// Called from the focusin handler in bloomEditing.ts for every focused
// editable. Posts the field's language to keyboarding/fieldFocused on every
// call (C# needs this notification to switch the OS keyboard, whether or not
// KMW itself has anything new to do) and then either attaches KeymanWeb
// (useKmw) or gets out of the way.
export async function attachKeymanWebIfNeeded(
    editable: HTMLElement,
): Promise<void> {
    const lang = normalizeLang(editable.getAttribute("lang"));
    if (kSkippedLangs.has(lang)) {
        // Not a real writing system; nothing to resolve. Hide any OSK a
        // previous KMW field left up.
        hideOsk();
        return;
    }

    const response = await postJsonAsync("keyboarding/fieldFocused", {
        lang,
    });
    const info = response?.data as FieldKeyboardInfo | undefined;

    // Cache + broadcast for the keyboard indicator (see recordKeyboardInfo),
    // whether or not KMW itself has anything to do for this field.
    if (info) recordKeyboardInfo(editable, info);

    if (!info?.useKmw) {
        // C# has already switched the OS keyboard; KMW has nothing to do for
        // this field. Don't load the engine just to hide the OSK — if it was
        // never loaded this session there is nothing to hide.
        hideOsk();
        return;
    }

    await attachKmwKeyboard(editable, lang, info);
}
