import {
    buildProcessChain,
    classifyProcesses,
    fetchBloomInstanceInfo,
    fetchJsonEndpoint,
    findRunningStandardBloomInstances,
    getDefaultRepoRoot,
    getWindowsProcessSnapshot,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireTcpPortOption,
    toLocalOrigin,
    toWorkspaceTabsEndpoint,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        json: false,
        runningBloom: false,
        repoRoot: getDefaultRepoRoot(),
        httpPort: undefined,
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--running-bloom") {
            options.runningBloom = true;
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
    }

    return options;
};

const options = parseArgs();
const processState = classifyProcesses(options.repoRoot);
const runningBloomInstances = options.runningBloom
    ? await findRunningStandardBloomInstances()
    : [];

let selectedRunningBloomInstance;
if (options.httpPort) {
    const instanceInfo = await fetchBloomInstanceInfo(options.httpPort);
    if (instanceInfo.reachable && instanceInfo.json) {
        selectedRunningBloomInstance = normalizeBloomInstanceInfo(
            instanceInfo.json,
            options.httpPort,
        );
    }
} else if (options.runningBloom) {
    selectedRunningBloomInstance = runningBloomInstances[0];
}

let selectedRunningBloomProcess;
if (selectedRunningBloomInstance?.processId) {
    const { byId } = getWindowsProcessSnapshot();
    const processRecord = byId.get(selectedRunningBloomInstance.processId);

    if (processRecord) {
        const processChain = buildProcessChain(processRecord, byId);
        const detectedRepoRoot = processChain.find(
            (entry) => entry.repoRoot,
        )?.repoRoot;

        selectedRunningBloomProcess = {
            processId: processRecord.processId,
            name: processRecord.name,
            executablePath: processRecord.executablePath,
            commandLine: processRecord.commandLine,
            detectedRepoRoot,
            matchesExpectedRepoRoot:
                !!detectedRepoRoot &&
                detectedRepoRoot.toLowerCase() ===
                    processState.expectedRepoRoot?.toLowerCase(),
            processChain,
        };
    }
}

const workspaceTabsEndpoint = selectedRunningBloomInstance?.httpPort
    ? toWorkspaceTabsEndpoint(selectedRunningBloomInstance.httpPort)
    : options.httpPort
      ? toWorkspaceTabsEndpoint(options.httpPort)
      : undefined;
const cdpVersionEndpoint = selectedRunningBloomInstance?.cdpPort
    ? `${toLocalOrigin(selectedRunningBloomInstance.cdpPort)}/json/version`
    : undefined;
const workspaceTabs = workspaceTabsEndpoint
    ? await fetchJsonEndpoint(workspaceTabsEndpoint)
    : {
          reachable: false,
          statusCode: undefined,
          json: undefined,
          error: "No workspace tabs endpoint was available.",
      };
const cdpVersion = cdpVersionEndpoint
    ? await fetchJsonEndpoint(cdpVersionEndpoint)
    : {
          reachable: false,
          statusCode: undefined,
          json: undefined,
          error: "No CDP endpoint was available.",
      };

const result = {
    mode: options.httpPort
        ? "explicit-http-port"
        : options.runningBloom
          ? "running-bloom"
          : "current-worktree",
    expectedRepoRoot: processState.expectedRepoRoot,
    requestedHttpPort: options.httpPort,
    isRunning: selectedRunningBloomInstance
        ? true
        : processState.bloomProcesses.length > 0,
    runningFromExpectedRepoRoot: processState.bloomProcesses.some(
        (processRecord) => processRecord.matchesExpectedRepoRoot,
    ),
    runningFromDifferentRepoRoot: processState.bloomProcesses.some(
        (processRecord) => !processRecord.matchesExpectedRepoRoot,
    ),
    bloomProcesses: processState.bloomProcesses,
    watchProcesses: processState.watchProcesses,
    ambiguousWatchProcesses: processState.ambiguousWatchProcesses,
    runningBloomInstances,
    selectedRunningBloomInstance,
    selectedRunningBloomProcess,
    endpoints: {
        workspaceTabs,
        cdpVersion,
    },
};

if (options.json) {
    console.log(JSON.stringify(result, null, 2));
    process.exit(0);
}

console.log(`Expected repo root: ${result.expectedRepoRoot}`);
console.log(`Bloom running: ${result.isRunning}`);
if (selectedRunningBloomInstance) {
    console.log(
        `Selected Bloom HTTP port: ${selectedRunningBloomInstance.httpPort}`,
    );
    console.log(
        `Selected Bloom CDP port: ${selectedRunningBloomInstance.cdpPort || "unknown"}`,
    );
    console.log(
        `Selected Bloom PID: ${selectedRunningBloomInstance.processId || "unknown"}`,
    );
}
console.log(
    `Running from expected repo root: ${result.runningFromExpectedRepoRoot}`,
);
console.log(
    `Running from different repo root: ${result.runningFromDifferentRepoRoot}`,
);
console.log(
    `Workspace tabs endpoint reachable: ${result.endpoints.workspaceTabs.reachable}`,
);
console.log(`CDP endpoint reachable: ${result.endpoints.cdpVersion.reachable}`);

for (const bloomProcess of result.bloomProcesses) {
    console.log("");
    console.log(`Bloom PID ${bloomProcess.processId}`);
    console.log(`Executable: ${bloomProcess.executablePath || "unknown"}`);
    console.log(
        `Detected repo root: ${bloomProcess.detectedRepoRoot || "unknown"}`,
    );
    console.log(
        `Matches expected repo root: ${bloomProcess.matchesExpectedRepoRoot}`,
    );

    for (const chainEntry of bloomProcess.processChain) {
        console.log(`  [${chainEntry.processId}] ${chainEntry.name}`);
        if (chainEntry.repoRoot) {
            console.log(`    repoRoot: ${chainEntry.repoRoot}`);
        }
        if (chainEntry.commandLine) {
            console.log(`    commandLine: ${chainEntry.commandLine}`);
        }
    }
}

if (result.bloomProcesses.length === 0) {
    console.log("No Bloom.exe process found.");
}

if (result.ambiguousWatchProcesses.length > 0) {
    console.log("");
    console.log(
        `Ambiguous watch processes: ${result.ambiguousWatchProcesses.length} (relative paths; not attributed to a repo root)`,
    );
}
