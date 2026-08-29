import { defineConfig } from "vitest/config";

// This suite is unusual: it drives a running Bloom over HTTP, and each test case switches tabs,
// stages a whole BloomPUB, and screenshots several bloom-player pages. That is far slower than a
// normal unit test, and Bloom writes into the collections/ tree while it runs.
export default defineConfig({
    test: {
        // vitest's 5s default is much too short here — a single case routinely does a tab switch,
        // a full BloomPUB staging, and several player-page captures (each with a networkidle wait
        // and a settle delay). Without this, healthy-but-slow cases fail as spurious timeouts.
        // Override per-run with --test-timeout when debugging under a breakpoint (see testPatient).
        // The calendar suite pushes this further still: making a book from the 24-page Wall
        // Calendar template and opening it in the edit tab has been seen to take over a minute
        // on its own, before the case does anything.
        testTimeout: 400000,
        // beforeAll may launch Bloom (up to ~60s) and afterAll resets branding/theme on every book,
        // so the setup/teardown hooks also need far more than the 10s default.
        hookTimeout: 180000,
        // One spec file at a time. Each of these suites launches a Bloom.exe of its own on a
        // throwaway collection copy; two of them starting at once would be two Blooms competing
        // for the same block of ports and for whatever the machine has, which is neither what
        // they are testing nor something we want a run to depend on.
        fileParallelism: false,
    },
    server: {
        watch: {
            // The suite makes Bloom write into collections/ (staged books, regenerated thumbnails,
            // books brought up to date, per-run screenshots). If vite's file watcher tries to watch
            // those files while Bloom briefly holds them open, it throws EBUSY on Windows and aborts
            // the whole run. We run non-watch (`vitest run`) anyway, but ignore these as a safety net
            // for anyone who starts a watch-mode run.
            ignored: ["**/collections/**", "**/screenshots/**"],
        },
    },
});
