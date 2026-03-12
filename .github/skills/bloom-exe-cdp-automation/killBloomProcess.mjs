import {
    classifyProcesses,
    getDefaultRepoRoot,
    killProcessIds,
} from "./bloomProcessCommon.mjs";

const args = process.argv.slice(2);
const json = args.includes("--json");
const onlyMismatched = args.includes("--only-mismatched");
const repoRootArgIndex = args.indexOf("--repo-root");
const expectedRepoRoot =
    repoRootArgIndex >= 0 ? args[repoRootArgIndex + 1] : getDefaultRepoRoot();

const processState = classifyProcesses(expectedRepoRoot);
const bloomProcesses = processState.bloomProcesses.filter(
    (processRecord) =>
        !onlyMismatched || !processRecord.matchesExpectedRepoRoot,
);
const fallbackWatchProcesses = processState.watchProcesses.filter(
    (processRecord) =>
        processRecord.detectedRepoRoot &&
        (!onlyMismatched || !processRecord.matchesExpectedRepoRoot),
);

const processIds = new Set();

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

const requestedProcessIds = [...processIds].sort((left, right) => right - left);
const killedProcessIds = killProcessIds(requestedProcessIds);

const result = {
    expectedRepoRoot: processState.expectedRepoRoot,
    onlyMismatched,
    requestedProcessIds,
    killedProcessIds,
};

if (json) {
    console.log(JSON.stringify(result, null, 2));
    process.exit(0);
}

if (killedProcessIds.length === 0) {
    console.log("No Bloom-related processes were killed.");
    process.exit(0);
}

console.log(`Killed process IDs: ${killedProcessIds.join(", ")}`);
