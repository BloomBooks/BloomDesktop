// Optional: confine every launched Bloom window to one monitor, so E2E runs don't take over
// the developer's working screens. Opt-in via the BLOOM_E2E_SCREEN environment variable —
// a 1-based screen number counting monitors left-to-right by X coordinate. Unset = current
// behavior (windows appear wherever Windows puts them).
//
// List your screens (same left-to-right order this uses):
//   powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | Sort-Object {$_.Bounds.X} | ForEach-Object {$i=1} { \"$i. $($_.DeviceName) $($_.Bounds) primary=$($_.Primary)\"; $i++ }"
//
// Implementation: one detached PowerShell watcher per Bloom instance (watchWindowScreen.ps1)
// polls the process's top-level windows and moves any stray onto the target screen. A poll,
// not a one-shot, because Bloom creates NEW top-level windows after launch (splash, the
// post-reopen Shell that createCloudTeamCollection causes, WinForms dialogs). The watcher
// exits by itself when the Bloom PID dies, so leaks are impossible even if stop() is missed.
//
// Caveat worth knowing: Bloom saves its window position to the shared per-machine user.config
// on exit, so after an E2E run the developer's OWN next Bloom launch may open on the E2E
// screen once. Harmless, but surprising the first time.
import { spawn, execFileSync } from "node:child_process";
import * as path from "node:path";

const watcherScript = path.join(__dirname, "watchWindowScreen.ps1");

// Fallback for a real footgun: setting BLOOM_E2E_SCREEN as a Windows user/machine
// environment variable does NOT reach processes whose parent shell started before the
// variable was set (long-lived agent shells, IDE terminals, service-launched runners). When
// the inherited environment lacks the variable, ask the registry-backed stores directly so
// "I set the variable, why is it ignored?" can't happen. Read once per test process.
let registryLookupDone = false;
let registryValue: string | undefined;
const envOrRegistry = (): string | undefined => {
    if (process.env.BLOOM_E2E_SCREEN) return process.env.BLOOM_E2E_SCREEN;
    if (!registryLookupDone) {
        registryLookupDone = true;
        try {
            const value = execFileSync(
                "powershell",
                [
                    "-NoProfile",
                    "-Command",
                    "[Environment]::GetEnvironmentVariable('BLOOM_E2E_SCREEN','User'), [Environment]::GetEnvironmentVariable('BLOOM_E2E_SCREEN','Machine') | Where-Object { $_ } | Select-Object -First 1",
                ],
                { timeout: 15_000, encoding: "utf8" },
            ).trim();
            registryValue = value || undefined;
        } catch {
            registryValue = undefined;
        }
    }
    return registryValue;
};

/** The 1-based screen index from BLOOM_E2E_SCREEN (inherited environment, falling back to
 * the user/machine registry value), or undefined when the feature is off. */
export const configuredScreenIndex = (): number | undefined => {
    const raw = envOrRegistry();
    if (!raw) return undefined;
    const index = Number(raw);
    if (!Number.isInteger(index) || index < 1) {
        throw new Error(
            `BLOOM_E2E_SCREEN must be a positive screen number (got '${raw}'). ` +
                `See harness/windowPlacement.ts for how to list screens.`,
        );
    }
    return index;
};

/** Starts the window-placement watcher for one Bloom instance if BLOOM_E2E_SCREEN is set.
 * Returns a stop function (safe to call more than once; also safe to never call, since the
 * watcher exits when the Bloom PID dies). */
export const startWindowPlacementWatcher = (
    bloomProcessId: number,
    logPath?: string,
): (() => void) => {
    const screenIndex = configuredScreenIndex();
    if (!screenIndex) return () => {};
    const args = [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        watcherScript,
        "-TargetPid",
        String(bloomProcessId),
        "-ScreenIndex",
        String(screenIndex),
    ];
    // The watcher appends its decisions (target screen resolution, every move) here, so
    // misplacement is diagnosable from artifacts instead of live observation.
    if (logPath) args.push("-LogPath", logPath);
    // detached MUST be false: spawning powershell with detached+windowsHide makes it exit
    // code 0 within ~1s having executed NOTHING (empirically bisected 10 Jul 2026 — the
    // DETACHED_PROCESS and CREATE_NO_WINDOW console flags conflict for a console app). This
    // silent failure meant the watcher NEVER ran from the day this feature was added; every
    // "windows on the wrong monitor" report traces back to it. Non-detached loses nothing:
    // the watcher still self-exits when the Bloom PID dies, and stop() still kills it.
    const watcher = spawn("powershell", args, {
        detached: false,
        stdio: "ignore",
        windowsHide: true,
    });
    watcher.unref(); // never keep the test process alive on its account
    return () => {
        try {
            watcher.kill();
        } catch {
            // already gone — fine, it self-exits when the Bloom PID dies
        }
    };
};
