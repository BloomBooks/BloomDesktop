// Look at files on disk from a test: fingerprints for "did Bloom change anything here", and path
// containment. Nothing here talks to Bloom.

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as Path from "node:path";

/**
 * A fingerprint of every file under `folder`: relative path to a SHA-1 of its bytes. Two calls
 * that return equal records mean nothing under the folder was added, removed, or rewritten; a
 * test uses that to show Bloom left a folder alone.
 */
export function fingerprintFolder(folder: string): Record<string, string> {
    const result: Record<string, string> = {};
    const walk = (dir: string) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = Path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else {
                result[Path.relative(folder, full).replace(/\\/g, "/")] = crypto
                    .createHash("sha1")
                    .update(fs.readFileSync(full))
                    .digest("hex");
            }
        }
    };
    walk(folder);
    return result;
}

/** True when `child` is `parent` or lies somewhere beneath it, ignoring case and slash style. */
export function isInsideFolder(child: string, parent: string): boolean {
    const relative = Path.relative(
        Path.resolve(parent).toLowerCase(),
        Path.resolve(child).toLowerCase(),
    );
    return (
        relative === "" ||
        (!relative.startsWith("..") && !Path.isAbsolute(relative))
    );
}
