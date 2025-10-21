import { spawn } from "node:child_process";
import * as path from "node:path";

const [, , componentArg] = process.argv;
const componentName = componentArg;
const defaultBaseUrl = "http://127.0.0.1:5173";

const resolveConfigPath = (): string =>
    path.resolve(process.cwd(), "vite.config.ts");

const startDevServer = (
    name: string | undefined,
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

    const child = spawn(process.execPath, args, {
        stdio: "inherit",
        env: process.env,
    });

    child.on("exit", (code) => {
        process.exit(code ?? 0);
    });

    return child;
};

const run = (): void => {
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

    const child = startDevServer(componentName);
};

run();
