import { defineConfig } from "vitest/config";
import { playwright } from "@vitest/browser-playwright";

// This is part of a so-far-unsuccessful attempt to get OverflowSpec.ts tests (and eventually others
// that need real layout calculations or otherwise need a real browser) to run under Vitest in browser mode.
// The current known problem is that vitest in browser mode has trouble resolving jQuery imports,
// for our very old version of jQuery.
// It ought to be called vitest.browser.config.ts, but when I do that, the VS Code extension just
// shows tests from here.

export default defineConfig({
    test: {
        include: ["**/OverflowSpec.ts"],
        setupFiles: ["./vitest.setup.ts"],
        browser: {
            enabled: true,
            provider: playwright(),
            headless: true,
            instances: [
                {
                    browser: "chromium",
                },
            ],
        },
    },
});
