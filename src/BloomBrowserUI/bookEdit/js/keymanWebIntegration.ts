// Loads the KeymanWeb engine from the Keyman CDN (once) and attaches the
// hard-coded Thai Kedmanee keyboard to Thai bloom-editable fields.
// Proof of concept.

const kKeymanEngineVersion = "18.0.249"; // current stable, verified on CDN 2026-07-08
// Base URL of the engine on the Keyman CDN. Used both to load keymanweb.js and
// as the engine "root": when we inject keymanweb.js dynamically, KeymanWeb cannot
// reliably infer its own base folder, so it resolves OSK resources (e.g. the
// on-screen-keyboard font osk/keymanweb-osk.ttf) relative to the page instead,
// which 404s against Bloom's server and raises a "Cannot Find File" dialog.
// Setting root explicitly points those resources back at the CDN.
const kKeymanEngineBaseUrl = `https://s.keyman.com/kmw/engine/${kKeymanEngineVersion}/`;

// The specific keyboard this POC turns on for Thai fields.
const kKeyboardId = "thai_kedmanee";
const kLanguageCode = "th";
// KeymanWeb prefixes the id with "Keyboard_" for the entry it exposes in
// getKeyboards(); that is the name we match on when checking load state.
const kKeyboardInternalName = `Keyboard_${kKeyboardId}`;

// Track attached elements in JS, NOT via a DOM attribute/class — the page DOM
// gets saved into the book, so we must not pollute it.
const attachedEditables = new WeakSet<HTMLElement>();

let keymanSetupPromise: Promise<any> | undefined;

// addKeyboards() only registers a keyboard STUB; the keyboard's actual code
// (its .js from the CDN) downloads lazily and is NOT present immediately after.
// Binding or activating the keyboard before that code has loaded makes
// KeymanWeb throw — verified empirically against the live engine: on a fresh
// load setKeyboardForControl throws "Cannot read properties of null (reading
// 'metadata')" and setActiveKeyboard can reject with "...keyboard script...may
// contain an error", because the loaded Keyboard object is still null. So we
// trigger the download and wait for the engine to mark the keyboard loaded
// before letting callers bind/activate it. (This is the real cause of the
// reported first-focus failure; the keyboard genuinely was not ready yet.)
const waitForThaiKeyboardLoaded = async (keyman: any): Promise<void> => {
    const isLoaded = () =>
        (keyman.getKeyboards() || []).some(
            (k: any) => k.InternalName === kKeyboardInternalName && k.HasLoaded,
        );
    // Activating the keyboard is what triggers its lazy code download. This call
    // can itself reject while the code is not present yet (exactly the race we
    // are guarding against), so ignore that rejection here; the poll below on the
    // engine's own HasLoaded flag is our real completion signal.
    keyman.setActiveKeyboard(kKeyboardId, kLanguageCode).catch(() => {});
    // Bounded poll (100ms x 50 = 5s). Empirically the keyboard loads in well
    // under a second; we fail fast rather than silently continuing with an
    // unusable keyboard if the CDN download never completes.
    for (let attempt = 0; attempt < 50; attempt++) {
        if (isLoaded()) return;
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(
        `KeymanWeb keyboard ${kKeyboardId} did not finish loading within 5s`,
    );
};

// Injects the KeymanWeb engine <script> the first time it is needed, then
// initializes it in "manual" attach mode (so we choose which controls it
// touches), registers the Thai Kedmanee keyboard, and waits for its code to
// finish downloading. Subsequent calls reuse the same promise, so the engine is
// loaded and initialized exactly once.
const ensureKeymanLoaded = (): Promise<any> => {
    if (!keymanSetupPromise) {
        keymanSetupPromise = new Promise<void>((resolve, reject) => {
            const script = document.createElement("script");
            script.src = `${kKeymanEngineBaseUrl}keymanweb.js`;
            script.onload = () => resolve();
            script.onerror = () =>
                reject(new Error("Failed to load keymanweb.js from CDN"));
            document.head.appendChild(script);
        })
            .then(() =>
                // Point ALL resource paths at the CDN. When keymanweb.js is
                // injected dynamically, the engine can't infer its own base and
                // otherwise resolves resources (notably the OSK font, loaded as
                // <fonts>/osk/keymanweb-osk.ttf) relative to the page — which
                // becomes lib/ckeditor//osk//keymanweb-osk.ttf and 404s against
                // Bloom's server, raising a "Cannot Find File" dialog. Setting
                // root alone did NOT redirect the OSK font (fonts did not inherit
                // it), so we set fonts and resources explicitly as well.
                // attachType "manual" = we choose which controls the engine touches.
                (window as any).keyman.init({
                    attachType: "manual",
                    root: kKeymanEngineBaseUrl,
                    resources: kKeymanEngineBaseUrl,
                    fonts: kKeymanEngineBaseUrl,
                }),
            )
            .then(() =>
                // Register the stub for the EXACT keyboard id we activate below.
                // The "@th" shorthand would instead register only the DEFAULT
                // keyboard for Thai (basic_kbdth0, "Thai Kedmanee Basic"), so a
                // subsequent setActiveKeyboard/setKeyboardForControl(
                // "thai_kedmanee", "th") would throw "No keyboard has been
                // registered with id thai_kedmanee".
                (window as any).keyman.addKeyboards({
                    id: kKeyboardId,
                    language: kLanguageCode,
                }),
            )
            // ...then make sure the keyboard's code has actually loaded before
            // any caller tries to bind or activate it (see comment above).
            .then(() => waitForThaiKeyboardLoaded((window as any).keyman))
            .then(() => (window as any).keyman);
    }
    return keymanSetupPromise;
};

// POC: hard-coded Thai; accept regional/script variants like th-TH.
const isThai = (lang: string | null): boolean =>
    lang === kLanguageCode || !!lang?.startsWith(`${kLanguageCode}-`);

// Called from the focusin handler in bloomEditing.ts for every focused editable.
// For Thai fields, lazily loads KeymanWeb and turns on the Thai Kedmanee
// keyboard. For non-Thai fields, ensures the Thai on-screen keyboard is not left
// showing, so the keyboard is active ONLY in Thai fields.
export const attachKeymanWebIfNeeded = async (editable: HTMLElement) => {
    const lang = editable.getAttribute("lang");
    if (!isThai(lang)) {
        // Hide the OSK a previous Thai field left up. Only meaningful once the
        // engine has loaded (a Thai field was focused earlier this session);
        // before that window.keyman is undefined and this is a no-op.
        (window as any).keyman?.osk?.hide?.();
        return;
    }
    // ensureKeymanLoaded resolves only once the keyboard's code has downloaded,
    // so the binding/activation calls below no longer race that download.
    const keyman = await ensureKeymanLoaded();

    // Attach + bind each control only once; doing it repeatedly is unnecessary.
    if (!attachedEditables.has(editable)) {
        attachedEditables.add(editable);
        keyman.attachToControl(editable);
        // Future focus events on this control select the keyboard automatically:
        keyman.setKeyboardForControl(editable, kKeyboardId, kLanguageCode);
    }

    // Activate the keyboard and show the OSK on EVERY Thai focus, not just the
    // first attach. Otherwise, once we hide the OSK on a non-Thai field, coming
    // back to an already-attached Thai field would leave the OSK hidden.
    // (Global activation is fine for the POC: non-Thai fields are never attached,
    // so KeymanWeb ignores them regardless of the globally active keyboard.)
    await keyman.setActiveKeyboard(kKeyboardId, kLanguageCode);
    // On desktop, activating a keyboard does not necessarily reveal the floating
    // on-screen keyboard, so ask for it explicitly. Guarded because the OSK API
    // surface can vary across engine builds and we don't want a POC to hard-fail
    // if it is absent.
    keyman.osk?.show?.(true);
};
