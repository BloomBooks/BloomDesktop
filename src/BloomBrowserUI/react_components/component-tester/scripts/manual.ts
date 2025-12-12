import { spawn } from "node:child_process";
import * as path from "node:path";
import { existsSync, readFileSync } from "node:fs";

// When called via yarn, the component name is passed as an argument after "--"
// We need to find it in process.argv
const rawArgs = process.argv
    .slice(2)
    .filter((arg) => arg !== "--" && arg.trim() !== "");

let backendRequested = false;
const positionalArgs = rawArgs.filter((arg) => {
    if (arg === "--backend") {
        backendRequested = true;
        return false;
    }
    return true;
});

let componentName: string | undefined = positionalArgs[0];
const defaultBaseUrl = "http://127.0.0.1:5183";

/**
 * Detects the component to load based on the current working directory.
 * Looks for a component-tester.config.ts file and reads the export name from it.
 */
const detectComponentFromDirectory = (): string | undefined => {
    const cwd = process.cwd();
    const componentTesterConfigPath = path.join(
        cwd,
        "component-tester.config.ts",
    );

    // First check if we're in a component test directory
    if (existsSync(componentTesterConfigPath)) {
        return getComponentNameFromConfig(componentTesterConfigPath);
    }

    // If we're in the component-tester directory, look at the INIT_CWD environment variable
    // which is set by yarn when running from a different directory
    const originalCwd = process.env.INIT_CWD;
    if (originalCwd) {
        const originalConfigPath = path.join(
            originalCwd,
            "component-tester.config.ts",
        );
        if (existsSync(originalConfigPath)) {
            return getComponentNameFromConfig(originalConfigPath);
        }
    }

    return undefined;
};

/**
 * Reads the component-tester.config.ts file and extracts the export name.
 */
const getComponentNameFromConfig = (configPath: string): string | undefined => {
    try {
        const content = readFileSync(configPath, "utf-8");
        // Match: exportName: "ComponentName" or exportName: 'ComponentName'
        const match = content.match(/exportName:\s*["']([^"']+)["']/);
        if (match && match[1]) {
            return match[1];
        }
    } catch {
        // If we can't read the file, just return undefined
    }
    return undefined;
};

const resolveConfigPath = (): string =>
    path.resolve(process.cwd(), "vite.config.ts");

const startDevServer = (
    name: string | undefined,
    options: {
        useBackend: boolean;
        backendUrl?: string;
    },
): import("node:child_process").ChildProcess => {
    const viteCli = path.resolve(
        process.cwd(),
        "node_modules",
        "vite",
        "bin",
        "vite.js",
    );
    const args = [viteCli, "--config", resolveConfigPath()];
    if (name) {
        args.push("--open", `/?component=${encodeURIComponent(name)}`);
    } else {
        args.push("--open", "/");
    }

    const childEnv = { ...process.env };
    if (options.useBackend) {
        childEnv.BLOOM_COMPONENT_TESTER_USE_BACKEND = "1";
        if (options.backendUrl) {
            childEnv.BLOOM_COMPONENT_TESTER_BACKEND_URL = options.backendUrl;
        }
    }

    const child = spawn(process.execPath, args, {
        stdio: "inherit",
        env: childEnv,
    });

    child.on("exit", (code) => {
        process.exit(code ?? 0);
    });

    return child;
};

const run = (): void => {
    // If no component name provided, try to detect it from the directory
    if (!componentName) {
        componentName = detectComponentFromDirectory();
    }

    const envBackendUrl = process.env.BLOOM_COMPONENT_TESTER_BACKEND_URL;
    const envUseBackend =
        process.env.BLOOM_COMPONENT_TESTER_USE_BACKEND === "1";
    const useBackend = backendRequested || envUseBackend;
    const backendUrl = envBackendUrl;

    if (!componentName) {
        console.log("Starting the dev server...");
        console.log(
            "Available components will be listed at the root URL after Vite starts.",
        );
    } else {
        const url = `${defaultBaseUrl}/?component=${encodeURIComponent(
            componentName,
        )}`;
        console.log(`Launching ${url}`);
    }

    if (useBackend) {
        const target = backendUrl ?? "http://localhost:8089";
        console.log(
            `Component tester will proxy Bloom API requests to ${target}.`,
        );
    }

    startDevServer(componentName, {
        useBackend,
        backendUrl,
    });
};

run();
