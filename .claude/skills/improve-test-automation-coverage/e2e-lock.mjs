// Run one command while holding the machine-wide Bloom e2e lock.
//
// Several worktrees on one machine must not run the BloomE2E suite at the same time: each run
// launches its own Bloom.exe, and concurrent instances still collide on shared user settings
// (see the bloom-multi-instance notes) and on the visual-regression baselines. This script makes
// the runs take turns. It is deliberately independent of any coordinator: a lock survives as long
// as the process that took it, and a lock whose owner process is gone is stale and gets removed.
//
// Usage (from src/BloomE2E of the worktree under test):
//
//     node <path-to-this-skill>/e2e-lock.mjs -- pnpm exec playwright test tests/my-test.spec.ts
//     node <path-to-this-skill>/e2e-lock.mjs --timeout-minutes 90 -- pnpm test
//
// The lock is the directory %LOCALAPPDATA%\Bloom\e2e-run.lock (mkdir is atomic on NTFS). Its
// owner.json records the holder's pid, start time and cwd, so a waiting agent can say who it is
// waiting for.

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { spawn } from "node:child_process";

const localAppData = process.env.LOCALAPPDATA;
if (!localAppData)
    throw new Error("LOCALAPPDATA is not set; this script expects Windows");
const lockDir = join(localAppData, "Bloom", "e2e-run.lock");
const ownerFile = join(lockDir, "owner.json");

const argv = process.argv.slice(2);
const separator = argv.indexOf("--");
if (separator < 0 || separator === argv.length - 1) {
    console.error(
        "usage: node e2e-lock.mjs [--timeout-minutes N] -- <command> [args...]",
    );
    process.exit(2);
}
const options = argv.slice(0, separator);
const command = argv.slice(separator + 1);
const timeoutIndex = options.indexOf("--timeout-minutes");
const timeoutMinutes =
    timeoutIndex >= 0 ? Number(options[timeoutIndex + 1]) : 60;
if (!Number.isFinite(timeoutMinutes) || timeoutMinutes <= 0) {
    console.error(
        `--timeout-minutes needs a positive number, not ${options[timeoutIndex + 1]}`,
    );
    process.exit(2);
}
const timeoutMs = timeoutMinutes * 60_000;
const pollMs = 5_000;

const isAlive = (pid) => {
    try {
        process.kill(pid, 0);
        return true;
    } catch (e) {
        // EPERM means the process exists but is not ours; ESRCH means it is gone.
        return e.code === "EPERM";
    }
};

const readOwner = () => {
    try {
        return JSON.parse(readFileSync(ownerFile, "utf8"));
    } catch {
        return null;
    }
};

const tryAcquire = () => {
    try {
        // The parent may not exist on a machine where Bloom has not run yet; only the lock
        // directory itself must be created non-recursively, because that is the atomic step.
        mkdirSync(join(localAppData, "Bloom"), { recursive: true });
        mkdirSync(lockDir, { recursive: false });
    } catch (e) {
        if (e.code !== "EEXIST") throw e;
        return false;
    }
    writeFileSync(
        ownerFile,
        JSON.stringify(
            {
                pid: process.pid,
                started: new Date().toISOString(),
                cwd: process.cwd(),
                command,
            },
            null,
            2,
        ),
    );
    return true;
};

const release = () => {
    const owner = readOwner();
    if (owner && owner.pid === process.pid)
        rmSync(lockDir, { recursive: true, force: true });
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const acquire = async () => {
    const deadline = Date.now() + timeoutMs;
    let announced = false;
    while (true) {
        if (tryAcquire()) return;
        const owner = readOwner();
        if (owner && !isAlive(owner.pid)) {
            console.error(
                `[e2e-lock] removing stale lock held by dead pid ${owner.pid} (${owner.cwd})`,
            );
            rmSync(lockDir, { recursive: true, force: true });
            continue;
        }
        if (!owner) {
            // The directory exists but owner.json is not written yet, or was just removed. Retry soon.
            await sleep(500);
            continue;
        }
        if (!announced) {
            console.error(
                `[e2e-lock] waiting: pid ${owner.pid} has run e2e tests in ${owner.cwd} since ${owner.started}`,
            );
            announced = true;
        }
        if (Date.now() > deadline) {
            console.error(
                `[e2e-lock] gave up after ${timeoutMs / 60_000} minutes; the lock is still held by pid ${owner.pid}`,
            );
            process.exit(3);
        }
        await sleep(pollMs);
    }
};

await acquire();
console.error(`[e2e-lock] acquired; running: ${command.join(" ")}`);

// One command string with shell:true, so that pnpm/npx shims resolve the same way they do in a
// terminal. Quote an argument only when it contains whitespace.
const quote = (arg) => (/\s/.test(arg) ? `"${arg}"` : arg);
const child = spawn(command.map(quote).join(" "), {
    stdio: "inherit",
    shell: true,
});
const forward = (signal) => () => child.kill(signal);
process.on("SIGINT", forward("SIGINT"));
process.on("SIGTERM", forward("SIGTERM"));
child.on("exit", (code, signal) => {
    release();
    process.exit(code ?? (signal ? 1 : 0));
});
process.on("exit", release);
