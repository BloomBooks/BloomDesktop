// Playwright globalSetup: builds Bloom exactly once for the whole test run (HARD-WON RULE #1
// — never rebuild while instances from earlier tests might still be starting/stopping).
// Individual spec files are responsible for resetting the stack and launching/killing their
// own instances per scenario.
import { buildBloomOnce } from "./launch";
import { resetLeakedWebView2Profiles } from "./reset";

export default async function globalSetup(): Promise<void> {
    // See reset.ts's doc comment: Bloom never cleans up its own WebView2 profile temp folders,
    // and letting them accumulate across a long session measurably slows down later launches.
    // eslint-disable-next-line no-console
    console.log(
        "[globalSetup] Clearing leaked WebView2 profile temp folders...",
    );
    await resetLeakedWebView2Profiles();

    // eslint-disable-next-line no-console
    console.log(
        "[globalSetup] Building Bloom.exe (Release) once for this test session...",
    );
    await buildBloomOnce();
    // eslint-disable-next-line no-console
    console.log("[globalSetup] Build complete.");
}
