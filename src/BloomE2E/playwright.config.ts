import { defineConfig } from "@playwright/test";

// Timeouts here are generous on purpose. Every number below is set by how long real things take,
// not by taste:
//
//  - Launching Bloom.exe and waiting for its server takes tens of seconds, and the WebView2 shell
//    takes tens more to mount. That happens once per worker, inside the fixture, so it counts
//    against the first test's timeout unless the timeout is large.
//  - Switching a workspace tab reloads a whole webview, which on a loaded machine is slow enough
//    that a 30-second wait has been a source of spurious failures elsewhere in this repo.
//
// A test that takes longer than these numbers is stuck, not slow.
export default defineConfig({
    testDir: "./tests",

    // One Bloom at a time. Several Bloom instances would race for the candidate HTTP ports and for
    // the settings each one writes, and the machine cannot usefully run two WebView2 shells at
    // once anyway.
    workers: 1,
    fullyParallel: false,

    // A retry would relaunch Bloom and hide exactly the flakiness we want to see. CI overrides
    // nothing here: if a test is unreliable, fix the test or file automation debt.
    retries: 0,

    // Per test. The first test in a file also pays for launching Bloom.
    timeout: 180000,

    expect: {
        // expect.poll / toPass loops that wait on Bloom's own state.
        timeout: 30000,
    },

    use: {
        // The first navigation after launch regularly exceeds Playwright's 30s default.
        navigationTimeout: 120000,
        actionTimeout: 30000,
        // We attach to Bloom's embedded WebView2, so nothing here launches a browser; these two
        // are for the tracing/screenshot machinery only.
        trace: "retain-on-failure",
        screenshot: "only-on-failure",
    },

    // The slow-steps reporter is on in every run, CI included: it prints one short list at the
    // end and nothing at all when no step was slow, and a step that has started taking seconds is
    // exactly the thing that otherwise goes unnoticed while the test still passes.
    reporter: process.env.CI
        ? [["list"], ["html", { open: "never" }], ["./reporters/slowSteps.ts"]]
        : [["list"], ["./reporters/slowSteps.ts"]],

    // Fail a CI run that was pushed with a stray test.only.
    forbidOnly: !!process.env.CI,
});
