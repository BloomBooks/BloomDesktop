// Playwright globalSetup: builds Bloom exactly once for the whole test run (HARD-WON RULE #1
// — never rebuild while instances from earlier tests might still be starting/stopping).
// Individual spec files are responsible for resetting the stack and launching/killing their
// own instances per scenario.
import { buildBloomOnce } from "./launch";

export default async function globalSetup(): Promise<void> {
    // eslint-disable-next-line no-console
    console.log(
        "[globalSetup] Building Bloom.exe (Release) once for this test session...",
    );
    await buildBloomOnce();
    // eslint-disable-next-line no-console
    console.log("[globalSetup] Build complete.");
}
