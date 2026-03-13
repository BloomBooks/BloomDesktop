import {
    buildProcessChain,
    classifyProcesses,
    fetchBloomInstanceInfo,
    fetchJsonEndpoint,
    findRunningStandardBloomInstances,
    getDefaultRepoRoot,
    getWindowsProcessSnapshot,
    normalizeBloomInstanceInfo,
} from "./bloomProcessCommon.mjs";

const parseArgs = () => {
    const args = process.argv.slice(2);
    const options = {
        json: false,
        runningBloom: false,
        repoRoot: getDefaultRepoRoot(),
        httpPort: undefined,
        cdpPort: undefined,
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
            options.httpPort = args[i + 1];
            i++;
            continue;
        }

        if (arg.startsWith("--http-port=")) {
            options.httpPort = arg.slice("--http-port=".length);
            continue;
        }

        if (arg === "--cdp-port") {
            options.cdpPort = args[i + 1];
            i++;
            continue;
        }

        if (arg.startsWith("--cdp-port=")) {
            options.cdpPort = arg.slice("--cdp-port=".length);
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
    const instanceInfo = await fetchBloomInstanceInfo(Number(options.httpPort));
    if (instanceInfo.reachable && instanceInfo.json) {
        selectedRunningBloomInstance = normalizeBloomInstanceInfo(
            instanceInfo.json,
            Number(options.httpPort),
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

const workspaceTabsUrl = selectedRunningBloomInstance
    ? selectedRunningBloomInstance.workspaceTabsUrl
    : options.httpPort
      ? `http://localhost:${Number(options.httpPort)}/bloom/api/workspace/tabs`
      : "http://localhost:8089/bloom/api/workspace/tabs";
const cdpVersionUrl = selectedRunningBloomInstance?.cdpOrigin
    ? `${selectedRunningBloomInstance.cdpOrigin}/json/version`
    : options.cdpPort
      ? `http://localhost:${Number(options.cdpPort)}/json/version`
      : "http://localhost:9222/json/version";
const workspaceTabs = await fetchJsonEndpoint(workspaceTabsUrl);
const cdpVersion = await fetchJsonEndpoint(cdpVersionUrl);

const result = {
    mode: options.httpPort
        ? "explicit-http-port"
        : options.runningBloom
          ? "running-bloom"
          : "current-worktree",
    expectedRepoRoot: processState.expectedRepoRoot,
    requestedHttpPort: options.httpPort ? Number(options.httpPort) : undefined,
    requestedCdpPort: options.cdpPort ? Number(options.cdpPort) : undefined,
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
