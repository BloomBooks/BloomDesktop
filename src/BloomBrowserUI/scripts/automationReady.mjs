/* eslint-env node */

// Parsing of the BLOOM_AUTOMATION_READY startup handshake for the cloud-TC
// build-once launcher (run.mjs). When Bloom is launched with --automation it
// prints a single stdout line of the form `BLOOM_AUTOMATION_READY {json}`
// (processId, httpPort, cdpPort, ...) once it is ready to be driven. (The
// repo-root scripts/watchBloomExe.mjs dotnet-watch launcher does its own
// equivalent scanning inline; run.mjs is a separate build-once launcher this
// cloud branch adds, so it keeps its scanner here.)

export const automationReadyPrefix = "BLOOM_AUTOMATION_READY ";

/**
 * Create a chunk-fed scanner for the BLOOM_AUTOMATION_READY handshake line.
 *
 * The returned function accepts raw stdout/stderr text chunks (as forwarded by
 * pipeChildOutput's onText, so chunks may split lines arbitrarily), buffers them
 * into lines, and for each line starting with the handshake prefix parses the
 * JSON payload and reports it via `onReady`.
 *
 * If parsing fails — or `onReady` itself throws (e.g. payload validation) — the
 * error is reported via `onParseError` so each caller can format its own
 * console message.
 *
 * @param {(info: object) => void} onReady - Called with the parsed payload of each handshake line.
 * @param {(error: Error) => void} onParseError - Called when a handshake line's payload cannot be parsed or handled.
 * @returns {(text: string) => void} The chunk-fed scanner.
 */
export const makeAutomationReadyScanner = (onReady, onParseError) => {
    let buffered = "";
    return (text) => {
        buffered += text;
        let newlineIndex;
        while ((newlineIndex = buffered.search(/\r\n|\r|\n/)) >= 0) {
            const line = buffered.slice(0, newlineIndex);
            buffered = buffered.slice(
                newlineIndex +
                    (buffered.startsWith("\r\n", newlineIndex) ? 2 : 1),
            );
            if (line.startsWith(automationReadyPrefix)) {
                try {
                    onReady(
                        JSON.parse(line.slice(automationReadyPrefix.length)),
                    );
                } catch (error) {
                    onParseError(error);
                }
            }
        }
    };
};
