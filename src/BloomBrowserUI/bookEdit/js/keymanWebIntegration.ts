// KeymanWeb (KMW) per-language keyboard integration. Implements plan item 5 of
// Design/Keyboards/keyboard-setting-plan.md: for every focused bloom-editable we
// ask the C# side (which does not exist yet — see plan item 4 — but whose
// contract is fixed) what this machine/collection wants for that field's
// language, and either let KMW take over typing for the field or get out of the
// way (C# has already switched the OS input method itself in that case).
//
// Supersedes the hard-coded Thai POC (commit af6f479a2).

import { postJsonAsync } from "../../utils/bloomApi";
import { setKmwAttached } from "../longPressShared";

// The engine is vendored (see keymanweb/README.md), not CDN-loaded — this is
// an offline app. BloomServer serves arbitrary disk files under /bloom/<path>,
// which is where the vendored src/BloomBrowserUI/keymanweb/ folder lands.
const kKeymanEngineFilesRoot = "/bloom/keymanweb/";

// Bloom's own pseudo-language markers (source-bubble/xmatter placeholders, "any
// language" fields). There is no writing system to resolve for these, so we
// never even ask the server about them.
const kSkippedLangs = new Set(["z", "*", ""]);

// The shape POST keyboarding/fieldFocused replies with (plan item 4's fixed
// contract; the C# endpoint doesn't exist yet, so we code against this).
interface FieldKeyboardInfo {
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
            script.src = `${kKeymanEngineFilesRoot}keymanweb.js`;
            script.onload = () => resolve();
            script.onerror = () =>
                reject(new Error("Failed to load keymanweb.js"));
            document.head.appendChild(script);
        })
            .then(() =>
                // Point ALL resource paths at our vendored folder. When
                // keymanweb.js is injected dynamically, the engine cannot reliably
                // infer its own base folder, so it resolves resources (e.g. the
                // on-screen-keyboard font osk/keymanweb-osk.ttf) relative to the
                // page instead, which 404s against Bloom's server and raises a
                // "Cannot Find File" dialog. Setting root alone did NOT redirect
                // the OSK font (fonts did not inherit it), so we set fonts and
                // resources explicitly too. attachType "manual" = we choose which
                // controls the engine touches.
                (window as any).keyman.init({
                    attachType: "manual",
                    root: kKeymanEngineFilesRoot,
                    resources: kKeymanEngineFilesRoot,
                    fonts: kKeymanEngineFilesRoot,
                }),
            )
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

    const alreadyRegistered =
        registeredKeyboardByLang.get(lang)?.keyboardId === info.keyboardId;
    if (!alreadyRegistered) {
        // Register the stub for the EXACT keyboard the server chose, using a
        // local file (object form = local stub, no cloud lookup) — the
        // collection has already cached this keyboard's .js (plan item 3).
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
        keyman.addKeyboards({
            id: info.keyboardId,
            filename: info.keyboardFileUrl,
            languages: [
                {
                    id: info.languageTag,
                    name: info.languageTag,
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
    // floating on-screen keyboard, so ask for it explicitly. Guarded because
    // the OSK API surface can vary across engine builds and we don't want a
    // missing method to break field focusing.
    keyman.osk?.show?.(true);
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
        // previous KMW field left up. Only meaningful once the engine has
        // loaded (some KMW field was focused earlier this session); before
        // that window.keyman is undefined and this is a no-op.
        (window as any).keyman?.osk?.hide?.();
        return;
    }

    const response = await postJsonAsync("keyboarding/fieldFocused", {
        lang,
    });
    const info = response?.data as FieldKeyboardInfo | undefined;

    if (!info?.useKmw) {
        // C# has already switched the OS keyboard; KMW has nothing to do for
        // this field. Don't load the engine just to hide the OSK — if it was
        // never loaded this session there is nothing to hide.
        (window as any).keyman?.osk?.hide?.();
        return;
    }

    await attachKmwKeyboard(editable, lang, info);
}
