import {
    buildProcessChain,
    classifyProcesses,
    fetchBloomInstanceInfo,
    getDefaultRepoRoot,
    getWindowsProcessSnapshot,
    killProcessIds,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        json: false,
        onlyMismatched: false,
        repoRoot: getDefaultRepoRoot(),
        httpPort: undefined,
        pid: undefined,
        watchPid: undefined,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--only-mismatched") {
            options.onlyMismatched = true;
            continue;
        }

        if (arg === "--repo-root") {
            options.repoRoot = args[i + 1] || options.repoRoot;
            i++;
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

        if (arg === "--pid") {
            options.pid = Number(args[i + 1]);
            i++;
            continue;
        }

        if (arg.startsWith("--pid=")) {
            options.pid = Number(arg.slice("--pid=".length));
            continue;
        }

        if (arg === "--watch-pid") {
            options.watchPid = Number(args[i + 1]);
            i++;
            continue;
        }

        if (arg.startsWith("--watch-pid=")) {
            options.watchPid = Number(arg.slice("--watch-pid=".length));
        }
    }

    return options;
};

const options = parseArgs();
const processState = classifyProcesses(options.repoRoot);
const processIds = new Set();
const exactTargetRequested =
    !!options.httpPort || !!options.pid || !!options.watchPid;
let targetedInstance;
let exactTargetResolutionError;

if (options.httpPort) {
    const instanceInfo = await fetchBloomInstanceInfo(options.httpPort);
    if (instanceInfo.reachable && instanceInfo.json) {
        targetedInstance = normalizeBloomInstanceInfo(
            instanceInfo.json,
            options.httpPort,
        );
        if (targetedInstance.processId) {
            processIds.add(targetedInstance.processId);
        }
    } else {
        exactTargetResolutionError = `No Bloom instance reported common/instanceInfo on http://localhost:${options.httpPort}.`;
    }
}

if (Number.isInteger(options.pid) && options.pid > 0) {
    processIds.add(options.pid);
}

if (Number.isInteger(options.watchPid) && options.watchPid > 0) {
    processIds.add(options.watchPid);
}

if (processIds.size > 0) {
    const { byId } = getWindowsProcessSnapshot();

    for (const requestedProcessId of [...processIds]) {
        const processRecord = byId.get(requestedProcessId);
        if (!processRecord) {
            continue;
        }

        const processChain = buildProcessChain(processRecord, byId);
        for (const chainEntry of processChain) {
            if (
                chainEntry.name === "Bloom.exe" ||
                (chainEntry.name === "dotnet.exe" &&
                    chainEntry.commandLine?.includes("BloomExe.csproj"))
            ) {
                processIds.add(chainEntry.processId);
            }
        }
    }
} else if (!exactTargetRequested) {
    const bloomProcesses = processState.bloomProcesses.filter(
        (processRecord) =>
            !options.onlyMismatched || !processRecord.matchesExpectedRepoRoot,
    );
    const fallbackWatchProcesses = processState.watchProcesses.filter(
        (processRecord) =>
            processRecord.detectedRepoRoot &&
            (!options.onlyMismatched || !processRecord.matchesExpectedRepoRoot),
    );

    for (const bloomProcess of bloomProcesses) {
        for (const chainEntry of bloomProcess.processChain) {
            if (
                chainEntry.name === "Bloom.exe" ||
                (chainEntry.name === "dotnet.exe" &&
                    chainEntry.commandLine?.includes("BloomExe.csproj"))
            ) {
                processIds.add(chainEntry.processId);
            }
        }
    }

    if (processIds.size === 0) {
        for (const watchProcess of fallbackWatchProcesses) {
            processIds.add(watchProcess.processId);
        }
    }
}

const requestedProcessIds = [...processIds].sort((left, right) => right - left);
const killedProcessIds = killProcessIds(requestedProcessIds);

const result = {
    expectedRepoRoot: processState.expectedRepoRoot,
    onlyMismatched: options.onlyMismatched,
    exactTargetRequested,
    exactTargetResolutionError,
    requestedHttpPort: options.httpPort,
    targetedInstance,
    requestedProcessIds,
    killedProcessIds,
};

if (options.json) {
    console.log(JSON.stringify(result, null, 2));
    process.exit(0);
}

if (exactTargetRequested && requestedProcessIds.length === 0) {
    console.log(
        exactTargetResolutionError ||
            "No explicit Bloom process target could be resolved.",
    );
    process.exit(1);
}

if (killedProcessIds.length === 0) {
    console.log("No Bloom-related processes were killed.");
    process.exit(0);
}

console.log(`Killed process IDs: ${killedProcessIds.join(", ")}`);
