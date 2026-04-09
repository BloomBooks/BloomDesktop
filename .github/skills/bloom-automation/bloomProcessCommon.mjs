import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const standardBloomStartingHttpPort = 8089;
const standardBloomReservedPortBlockLength = 3;
const standardBloomPortCount = 20;

export const toLocalOrigin = (port) => `http://localhost:${port}`;
export const toBloomApiBaseUrl = (port) => `${toLocalOrigin(port)}/bloom/api`;
export const toWorkspaceTabsEndpoint = (httpPort) =>
    `${toBloomApiBaseUrl(httpPort)}/workspace/tabs`;
const toPositiveInteger = (value) => {
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
};

export const toTcpPort = (value) => {
    if (value === undefined || value === null) {
        return undefined;
    }

    const normalized = String(value).trim();
    if (!/^\d+$/.test(normalized)) {
        return undefined;
    }

    const parsed = Number(normalized);
    return Number.isInteger(parsed) && parsed > 0 && parsed <= 65535
        ? parsed
        : undefined;
};

export const requireTcpPortOption = (optionName, value) => {
    const port = toTcpPort(value);
    if (!port) {
        throw new Error(
            `${optionName} must be an integer from 1 to 65535. Received: ${value}`,
        );
    }

    return port;
};

export const requireOptionValue = (args, index, optionName) => {
    const value = args[index + 1];
    if (!value || value.startsWith("--")) {
        throw new Error(`${optionName} requires a value.`);
    }

    return value;
};

export const getStandardBloomHttpPorts = () =>
    Array.from(
        { length: standardBloomPortCount },
        (_, index) =>
            standardBloomStartingHttpPort +
            index * standardBloomReservedPortBlockLength,
    );

export const getDefaultRepoRoot = () =>
    path.resolve(
        path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "..",
        "..",
    );

const normalizePath = (value) => {
    if (!value) {
        return undefined;
    }

    const trimmed = value.trim().replace(/^"|"$/g, "");
    if (!trimmed) {
        return undefined;
    }

    return path.resolve(trimmed).replace(/\//g, "\\");
};

export const extractRepoRoot = (text) => {
    if (!text) {
        return undefined;
    }

    const normalized = text.replace(/\//g, "\\");

    const projectMatch = normalized.match(
        /([A-Za-z]:\\[^"\r\n]+?)\\src\\BloomExe\\BloomExe\.csproj/i,
    );
    if (projectMatch?.[1]) {
        return normalizePath(projectMatch[1]);
    }

    const exeMatch = normalized.match(
        /([A-Za-z]:\\[^"\r\n]+?)\\output\\[^"\r\n]+?\\Bloom\.exe/i,
    );
    if (exeMatch?.[1]) {
        return normalizePath(exeMatch[1]);
    }

    return undefined;
};

export const normalizeBloomInstanceInfo = (info, discoveredViaPort) => {
    const httpPort = toTcpPort(info?.httpPort) ?? discoveredViaPort;
    const cdpPort = toTcpPort(info?.cdpPort);
    const executablePath = normalizePath(info?.executablePath);
    const detectedRepoRoot = extractRepoRoot(executablePath);
    const vitePort = toTcpPort(info?.vitePort);

    // Normalize old and new discovery payloads into one shape so the CLI helpers can stay simple.
    return {
        processId: toPositiveInteger(info?.processId),
        discoveredViaPort,
        httpPort,
        origin: toLocalOrigin(httpPort),
        cdpPort,
        executablePath,
        detectedRepoRoot,
        vitePort,
    };
};

const parseWmicList = (text) => {
    const lines = text.replace(/\r/g, "").split("\n");
    const records = [];
    let current = {};

    const flush = () => {
        if (Object.keys(current).length > 0) {
            records.push(current);
            current = {};
        }
    };

    for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed) {
            flush();
            continue;
        }

        const equalsIndex = trimmed.indexOf("=");
        if (equalsIndex < 0) {
            continue;
        }

        const key = trimmed.slice(0, equalsIndex);
        const value = trimmed.slice(equalsIndex + 1);
        current[key] = value;
    }

    flush();
    return records;
};

const queryProcessesByName = (name) => {
    for (let attempt = 0; attempt < 3; attempt++) {
        try {
            const output = execFileSync(
                "wmic",
                [
                    "process",
                    "where",
                    `name='${name}'`,
                    "get",
                    "ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine",
                    "/format:list",
                ],
                {
                    encoding: "utf8",
                    timeout: 5000,
                    windowsHide: true,
                },
            );
            return parseWmicList(output);
        } catch (error) {
            if (attempt === 2) {
                return [];
            }
        }
    }

    return [];
};

export const getWindowsProcessSnapshot = () => {
    const rawProcesses = [
        ...queryProcessesByName("Bloom.exe"),
        ...queryProcessesByName("dotnet.exe"),
    ]
        .map((record) => ({
            processId: Number(record.ProcessId || 0),
            parentProcessId: Number(record.ParentProcessId || 0),
            name: record.Name,
            executablePath: record.ExecutablePath || undefined,
            commandLine: record.CommandLine || undefined,
        }))
        .filter((record) => record.processId > 0 && record.name);

    const byId = new Map(
        rawProcesses.map((record) => [record.processId, record]),
    );
    return { rawProcesses, byId };
};

export const buildProcessChain = (processRecord, byId) => {
    const chain = [];
    let current = processRecord;

    for (let i = 0; i < 8 && current; i++) {
        chain.push({
            processId: current.processId,
            parentProcessId: current.parentProcessId,
            name: current.name,
            executablePath: current.executablePath,
            commandLine: current.commandLine,
            repoRoot:
                extractRepoRoot(current.executablePath) ||
                extractRepoRoot(current.commandLine),
        });

        current = byId.get(current.parentProcessId);
    }

    return chain;
};

export const classifyProcesses = (expectedRepoRoot) => {
    const normalizedExpectedRepoRoot = normalizePath(expectedRepoRoot);
    const { rawProcesses, byId } = getWindowsProcessSnapshot();

    const toRecord = (processRecord) => {
        const processChain = buildProcessChain(processRecord, byId);
        const detectedRepoRoot = processChain.find(
            (entry) => entry.repoRoot,
        )?.repoRoot;

        return {
            processId: processRecord.processId,
            name: processRecord.name,
            executablePath: processRecord.executablePath,
            commandLine: processRecord.commandLine,
            detectedRepoRoot,
            matchesExpectedRepoRoot:
                !!detectedRepoRoot &&
                !!normalizedExpectedRepoRoot &&
                detectedRepoRoot.toLowerCase() ===
                    normalizedExpectedRepoRoot.toLowerCase(),
            processChain,
        };
    };

    const bloomProcesses = rawProcesses
        .filter((processRecord) => processRecord.name === "Bloom.exe")
        .map(toRecord);

    const rawWatchProcesses = rawProcesses
        .filter(
            (processRecord) =>
                processRecord.name === "dotnet.exe" &&
                processRecord.commandLine?.includes("BloomExe.csproj") &&
                (processRecord.commandLine.includes("dotnet-watch.dll") ||
                    processRecord.commandLine.includes("DOTNET_WATCH=1") ||
                    processRecord.commandLine.includes(" watch run ")),
        )
        .map(toRecord);

    const watchProcesses = rawWatchProcesses.filter(
        (processRecord) => processRecord.detectedRepoRoot,
    );
    const ambiguousWatchProcesses = rawWatchProcesses.filter(
        (processRecord) => !processRecord.detectedRepoRoot,
    );

    return {
        expectedRepoRoot: normalizedExpectedRepoRoot,
        bloomProcesses,
        watchProcesses,
        ambiguousWatchProcesses,
    };
};

export const fetchJsonEndpoint = async (url) => {
    try {
        const response = await fetch(url);
        const body = await response.text();
        return {
            reachable: response.ok,
            statusCode: response.status,
            json: body ? JSON.parse(body) : undefined,
            error: response.ok
                ? undefined
                : `${response.status} ${response.statusText}`,
        };
    } catch (error) {
        return {
            reachable: false,
            statusCode: undefined,
            json: undefined,
            error: error instanceof Error ? error.message : String(error),
        };
    }
};

export const fetchBloomInstanceInfo = async (httpPort) =>
    fetchJsonEndpoint(`${toBloomApiBaseUrl(httpPort)}/common/instanceInfo`);

export const waitForBloomInstanceInfo = async (httpPort, timeoutMs = 30000) => {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
        const response = await fetchBloomInstanceInfo(httpPort);
        if (response.reachable && response.json) {
            return normalizeBloomInstanceInfo(response.json, httpPort);
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
    }

    throw new Error(
        `Bloom did not report common/instanceInfo on http://localhost:${httpPort} within ${timeoutMs} ms.`,
    );
};

export const findRunningStandardBloomInstances = async () => {
    const responses = await Promise.all(
        getStandardBloomHttpPorts().map(async (port) => ({
            port,
            instanceInfo: await fetchBloomInstanceInfo(port),
        })),
    );

    return responses
        .filter(
            ({ instanceInfo }) => instanceInfo.reachable && !!instanceInfo.json,
        )
        .map(({ port, instanceInfo }) =>
            normalizeBloomInstanceInfo(instanceInfo.json, port),
        )
        .sort((left, right) => left.httpPort - right.httpPort);
};

export const findRunningStandardBloomInstance = async () => {
    const instances = await findRunningStandardBloomInstances();
    return instances[0];
};

export const killProcessIds = (processIds) => {
    const killed = [];

    for (const processId of processIds) {
        try {
            execFileSync("taskkill", ["/PID", String(processId), "/F"], {
                encoding: "utf8",
                stdio: "pipe",
            });
            killed.push(processId);
        } catch {}
    }

    return killed;
};
