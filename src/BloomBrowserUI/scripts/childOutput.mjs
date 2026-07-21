/* eslint-env node */
/* global process */

// Line-buffered, prefixed forwarding of a child process's stdout/stderr to our
// own streams. Extracted from go.mjs so both the watch launcher (go.mjs) and the
// build-once launcher (run.mjs) share one implementation. `onText` (optional) is
// invoked with each raw chunk before it is line-split, so a caller can watch the
// stream for readiness markers.

const createPrefixedWriter = (prefix, target, onText) => {
    let buffered = "";

    const flushLines = (text) => {
        buffered += text;
        let lineStart = 0;

        for (let index = 0; index < buffered.length; index++) {
            const current = buffered[index];
            if (current === "\n") {
                target.write(`${prefix}${buffered.slice(lineStart, index)}\n`);
                lineStart = index + 1;
                continue;
            }

            if (current !== "\r") {
                continue;
            }

            if (index === buffered.length - 1) {
                break;
            }

            target.write(`${prefix}${buffered.slice(lineStart, index)}\n`);
            if (buffered[index + 1] === "\n") {
                index++;
            }

            lineStart = index + 1;
        }

        buffered = buffered.slice(lineStart);
    };

    return {
        write: (chunk) => {
            const text = chunk.toString();
            onText?.(text);
            flushLines(text);
        },
        flush: () => {
            const remainingLine = buffered.endsWith("\r")
                ? buffered.slice(0, -1)
                : buffered;
            if (!remainingLine) {
                buffered = "";
                return;
            }

            target.write(`${prefix}${remainingLine}\n`);
            buffered = "";
        },
    };
};

/**
 * Forward a child process's stdout and stderr to this process's stdout/stderr,
 * prefixing every line with `prefix` (e.g. "[dev] ") so interleaved output from
 * multiple children stays legible. Optionally calls `onText` with each raw chunk.
 *
 * @param {import("node:child_process").ChildProcess} child - The child whose output to forward.
 * @param {string} prefix - Text prepended to every forwarded line.
 * @param {(text: string) => void} [onText] - Optional observer of each raw chunk.
 */
export const pipeChildOutput = (child, prefix, onText) => {
    const stdoutWriter = createPrefixedWriter(prefix, process.stdout, onText);
    const stderrWriter = createPrefixedWriter(prefix, process.stderr, onText);

    child.stdout.on("data", stdoutWriter.write);
    child.stderr.on("data", stderrWriter.write);
    child.stdout.on("end", stdoutWriter.flush);
    child.stderr.on("end", stderrWriter.flush);
};
