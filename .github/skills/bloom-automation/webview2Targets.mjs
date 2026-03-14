import {
    fetchBloomInstanceInfo,
    findRunningStandardBloomInstance,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        host: "localhost",
        port: "9222",
        json: false,
        all: false,
        runningBloom: false,
        httpPort: undefined,
        wait: false,
        timeoutMs: 15000,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];
        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--all") {
            options.all = true;
            continue;
        }

        if (arg === "--running-bloom") {
            options.runningBloom = true;
            continue;
        }

        if (arg === "--http-port") {
            options.httpPort = requireTcpPortOption(
                "--http-port",
                requireOptionValue(args, i, "--http-port"),
            );
            i++;
            continue;
        }

        if (arg.startsWith("--http-port=")) {
            options.httpPort = requireTcpPortOption(
                "--http-port",
                arg.slice("--http-port=".length),
            );
            continue;
        }

        if (arg === "--wait") {
            options.wait = true;
            continue;
        }

        if (arg === "--host") {
            options.host = args[i + 1] || options.host;
            i++;
            continue;
        }

        if (arg === "--port") {
            options.port = requireTcpPortOption(
                "--port",
                requireOptionValue(args, i, "--port"),
            );
            i++;
            continue;
        }

        if (arg.startsWith("--port=")) {
            options.port = requireTcpPortOption(
                "--port",
                arg.slice("--port=".length),
            );
            continue;
        }

        if (arg === "--timeout-ms") {
            options.timeoutMs = Number(args[i + 1] || options.timeoutMs);
            i++;
        }
    }

    return options;
};

const fetchJson = async (url) => {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(
            `Request failed: ${response.status} ${response.statusText} for ${url}`,
        );
    }

    return response.json();
};

const delay = async (ms) => {
    await new Promise((resolve) => setTimeout(resolve, ms));
};

const isLikelyBloomTarget = (target) => {
    if (target.type !== "page") {
        return false;
    }

    if (!target.url || target.url.startsWith("devtools://")) {
        return false;
    }

    return (
        target.url.includes("/bloom/") ||
        target.title?.includes("InMemoryHtmlFile") ||
        target.title?.includes("Bloom")
    );
};

const normalizeFrontendUrl = (origin, frontendUrl) => {
    if (!frontendUrl) {
        return undefined;
    }

    if (
        frontendUrl.startsWith("http://") ||
        frontendUrl.startsWith("https://")
    ) {
        return frontendUrl;
    }

    return `${origin}${frontendUrl}`;
};

const printText = (result) => {
    console.log(`CDP endpoint: ${result.origin}`);
    console.log(`Browser: ${result.version.Browser || "unknown"}`);
    console.log(`Protocol: ${result.version["Protocol-Version"] || "unknown"}`);
    console.log(`Targets: ${result.targets.length}`);

    if (result.targets.length === 0) {
        console.log("No matching targets found.");
        console.log(
            "Try rerunning with --all if Bloom is open but did not match the default filter.",
        );
        return;
    }

    for (const target of result.targets) {
        console.log("");
        console.log(`[${target.id}] ${target.title || "(untitled)"}`);
        console.log(`type: ${target.type}`);
        console.log(`url: ${target.url}`);
        console.log(
            `webSocketDebuggerUrl: ${target.webSocketDebuggerUrl || ""}`,
        );
        if (target.devtoolsFrontendUrl) {
            console.log(`devtoolsFrontendUrl: ${target.devtoolsFrontendUrl}`);
        }
        if (target.likelyBloomTarget) {
            console.log("likelyBloomTarget: true");
        }
    }
};

const main = async () => {
    const options = parseArgs();
    const deadline = Date.now() + options.timeoutMs;

    let version;
    let filteredTargets = [];
    let origin = `http://${options.host}:${options.port}`;
    let runningBloomInstance;
    let selectedInstance;
    let lastError;

    while (true) {
        try {
            if (options.httpPort) {
                const instanceInfo = await fetchBloomInstanceInfo(
                    options.httpPort,
                );
                if (!instanceInfo.reachable || !instanceInfo.json) {
                    throw new Error(
                        `No Bloom instance reported common/instanceInfo on http://localhost:${options.httpPort}.`,
                    );
                }

                selectedInstance = normalizeBloomInstanceInfo(
                    instanceInfo.json,
                    options.httpPort,
                );
                if (!selectedInstance.cdpOrigin) {
                    throw new Error(
                        "The selected Bloom instance did not report a CDP endpoint.",
                    );
                }

                origin = selectedInstance.cdpOrigin;
            } else if (options.runningBloom) {
                runningBloomInstance = await findRunningStandardBloomInstance();
                if (!runningBloomInstance) {
                    throw new Error(
                        "No running Bloom instance was found on Bloom's standard HTTP port range.",
                    );
                }

                if (!runningBloomInstance.cdpOrigin) {
                    throw new Error(
                        "The running Bloom instance did not report a CDP endpoint.",
                    );
                }

                origin = runningBloomInstance.cdpOrigin;
            }

            const [currentVersion, targets] = await Promise.all([
                fetchJson(`${origin}/json/version`),
                fetchJson(`${origin}/json/list`),
            ]);

            version = currentVersion;
            const normalizedTargets = targets.map((target) => ({
                id: target.id,
                type: target.type,
                title: target.title,
                url: target.url,
                webSocketDebuggerUrl: target.webSocketDebuggerUrl,
                devtoolsFrontendUrl: normalizeFrontendUrl(
                    origin,
                    target.devtoolsFrontendUrl,
                ),
                likelyBloomTarget: isLikelyBloomTarget(target),
            }));

            filteredTargets = options.all
                ? normalizedTargets
                : normalizedTargets.filter(
                      (target) => target.likelyBloomTarget,
                  );

            if (
                !options.wait ||
                filteredTargets.length > 0 ||
                Date.now() >= deadline
            ) {
                break;
            }
        } catch (error) {
            lastError = error;
            if (!options.wait || Date.now() >= deadline) {
                throw error;
            }
        }

        await delay(250);
    }

    const result = {
        origin,
        version,
        targets: filteredTargets,
        primaryTarget: filteredTargets[0],
        runningBloomInstance: selectedInstance || runningBloomInstance,
        error: lastError instanceof Error ? lastError.message : undefined,
    };

    if (options.json) {
        console.log(JSON.stringify(result, null, 2));
        return;
    }

    printText(result);
};

main().catch((error) => {
    console.error(error.message);
    process.exit(1);
});
