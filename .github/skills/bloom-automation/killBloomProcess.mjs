import {
    buildProcessChain,
    classifyProcesses,
    fetchBloomInstanceInfo,
    getDefaultRepoRoot,
    getWindowsProcessSnapshot,
    killProcessIds,
    normalizeBloomInstanceInfo,
    requireOptionValue,
    requireProcessIdOption,
    requireTcpPortOption,
} from "./bloomProcessCommon.mjs";

const usage = `Kill the Bloom.exe (and dotnet.exe BloomExe.csproj) processes of this worktree.

  node killBloomProcess.mjs [options]

  --help, -h              Print this and exit without killing anything.
  --json                  Report what was killed as JSON.
  --only-mismatched       Kill only instances whose repo root is not this worktree.
  --repo-root <path>      The worktree to judge instances against (default: this checkout).
  --http-port <port>      Kill the instance whose server answers on this port.
  --pid <pid>             Kill this process and the Bloom processes in its chain.
  --watch-pid <pid>       Kill this launcher/watch process and its Bloom processes.

With no --http-port, --pid or --watch-pid, this kills EVERY Bloom this worktree owns.`;

// Print the usage and exit 0 without killing anything, or reject an unknown flag with a non-zero
// exit. Reading the usage first must never be the dangerous move: this script used to ignore
// --help and go straight to its destructive default (AUTOMATION-DEBT.md, "Automation helper
// scripts run destructive defaults on unknown flags").
const exitWithUsage = (unknownArgument) => {
    if (unknownArgument) {
        console.error(`Unknown option ${unknownArgument}.\n\n${usage}`);
        process.exit(2);
    }
    console.log(usage);
    process.exit(0);
};

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

        if (arg === "--help" || arg === "-h") {
            exitWithUsage();
        }

        if (arg === "--json") {
            options.json = true;
            continue;
        }

        if (arg === "--only-mismatched") {
            options.onlyMismatched = true;
            continue;
        }

        if (arg === "--repo-root") {
            // A required value, checked the same way as every other option's. Taking
            // `args[i + 1]` and falling back to the default would swallow the NEXT FLAG as
            // this option's value, so `--repo-root --pid 123` would name no target at all and
            // reach the default that kills every Bloom this worktree owns.
            options.repoRoot = requireOptionValue(args, i, "--repo-root");
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
            options.pid = requireProcessIdOption(
                "--pid",
                requireOptionValue(args, i, "--pid"),
            );
            i++;
            continue;
        }

        if (arg.startsWith("--pid=")) {
            options.pid = requireProcessIdOption(
                "--pid",
                arg.slice("--pid=".length),
            );
            continue;
        }

        if (arg === "--watch-pid") {
            options.watchPid = requireProcessIdOption(
                "--watch-pid",
                requireOptionValue(args, i, "--watch-pid"),
            );
            i++;
            continue;
        }

        if (arg.startsWith("--watch-pid=")) {
            options.watchPid = requireProcessIdOption(
                "--watch-pid",
                arg.slice("--watch-pid=".length),
            );
            continue;
        }

        exitWithUsage(arg);
    }

    return options;
};

const options = parseArgs();
const processState = classifyProcesses(options.repoRoot);
const processIds = new Set();
// Whether the caller named a target, not whether the value we parsed from it is usable. The
// two are the same now that every target option is validated at parse time, and this says the
// intended thing: a caller who asked for one process must never reach the default that kills
// every Bloom this worktree owns.
const exactTargetRequested =
    options.httpPort !== undefined ||
    options.pid !== undefined ||
    options.watchPid !== undefined;
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
