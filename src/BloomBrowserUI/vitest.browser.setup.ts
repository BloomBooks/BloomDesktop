// Setup for the "browser" vitest project (*.browser.spec.ts), which runs in a real browser.
// Do not import vitest.setup.ts here: its jsdom polyfills (a fake canvas getContext, an
// innerText built on textContent, ...) replace the real browser APIs that these tests exist
// to exercise.
import jQuery from "jquery";
import theOneLocalizationManager from "./lib/localizationManager/localizationManager";

// Legacy jQuery plugins that our modules import at load time expect a global jQuery.
globalThis.$ = jQuery;
globalThis.jQuery = jQuery;

// There is no Bloom to answer localization (or other API) requests.
theOneLocalizationManager.bypassLocalization();
