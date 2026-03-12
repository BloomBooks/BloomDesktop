import {
    classifyProcesses,
    fetchJsonEndpoint,
    findRunningStandardBloomInstances,
    getDefaultRepoRoot,
} from "./bloomProcessCommon.mjs";

const args = process.argv.slice(2);
const json = args.includes("--json");
const runningBloom = args.includes("--running-bloom");
const repoRootArgIndex = args.indexOf("--repo-root");
const expectedRepoRoot =
    repoRootArgIndex >= 0 ? args[repoRootArgIndex + 1] : getDefaultRepoRoot();

const processState = classifyProcesses(expectedRepoRoot);
const runningBloomInstances = runningBloom
    ? await findRunningStandardBloomInstances()
    : [];
const selectedRunningBloomInstance = runningBloom
    ? runningBloomInstances[0]
    : undefined;
const workspaceTabsUrl = selectedRunningBloomInstance
    ? selectedRunningBloomInstance.workspaceTabsUrl
    : "http://localhost:8089/bloom/api/workspace/tabs";
const cdpVersionUrl = selectedRunningBloomInstance?.cdpOrigin
    ? `${selectedRunningBloomInstance.cdpOrigin}/json/version`
    : "http://localhost:9222/json/version";
const workspaceTabs = await fetchJsonEndpoint(workspaceTabsUrl);
const cdpVersion = await fetchJsonEndpoint(cdpVersionUrl);

const result = {
    mode: runningBloom ? "running-bloom" : "current-worktree",
    expectedRepoRoot: processState.expectedRepoRoot,
    isRunning: processState.bloomProcesses.length > 0,
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
    endpoints: {
        workspaceTabs,
        cdpVersion,
    },
};

if (json) {
    console.log(JSON.stringify(result, null, 2));
    process.exit(0);
}

console.log(`Expected repo root: ${result.expectedRepoRoot}`);
console.log(`Bloom running: ${result.isRunning}`);
if (runningBloom) {
    console.log(
        `Running Bloom instances found: ${result.runningBloomInstances.length}`,
    );
    if (result.selectedRunningBloomInstance) {
        console.log(
            `Selected running Bloom HTTP port: ${result.selectedRunningBloomInstance.httpPort}`,
        );
        console.log(
            `Selected running Bloom executable: ${
                result.selectedRunningBloomInstance.executablePath || "unknown"
            }`,
        );
    }
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
