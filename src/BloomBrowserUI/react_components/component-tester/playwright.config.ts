import * as path from "path";
import type { PlaywrightTestConfig } from "@playwright/test";

// Force Node to resolve Playwright from this package's node_modules so sibling spec files
// don't pull in the repo-level copy and trigger the "Requiring @playwright/test second time" error.
// eslint-disable-next-line @typescript-eslint/no-require-imports, @typescript-eslint/no-var-requires
const moduleLoader = require("module") as {
    _initPaths?: () => void;
    _resolveFilename?: (
        request: string,
        parent: NodeModule | undefined,
        isMain: boolean,
        options: { paths?: string[] } | undefined,
    ) => string;
};

// Make sure our local node_modules is searched before anything inherited from the parent repo.
const nodeModulesPath = path.resolve(__dirname, "node_modules");
const currentNodePath = process.env.NODE_PATH
    ? process.env.NODE_PATH.split(path.delimiter)
    : [];

if (!currentNodePath.includes(nodeModulesPath)) {
    process.env.NODE_PATH = [nodeModulesPath, ...currentNodePath]
        .filter(Boolean)
        .join(path.delimiter);
    moduleLoader._initPaths?.();
}

// Resolve once so every import of @playwright/test goes through this exact file path.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const preferredPlaywrightPath = require.resolve("@playwright/test", {
    paths: [__dirname],
});

// eslint-disable-next-line @typescript-eslint/no-unused-vars
const originalResolveFilename =
    moduleLoader._resolveFilename?.bind(moduleLoader);

if (originalResolveFilename) {
    // Monkey-patch the resolver so Playwright's own workers reuse the same package instance.
    moduleLoader._resolveFilename = (
        request: string,
        parent: NodeModule | undefined,
        isMain: boolean,
        options: { paths?: string[] } | undefined,
    ): string => {
        if (request === "@playwright/test") {
            return preferredPlaywrightPath;
        }

        // Playwright's JSX transform tries to import playwright/jsx-runtime,
        // but we're using @emotion/react for JSX. Redirect to react/jsx-runtime instead.
        if (
            request === "playwright/jsx-runtime" ||
            request.includes("playwright/jsx-runtime") ||
            request.includes("playwright\\jsx-runtime")
        ) {
            return require.resolve("react/jsx-runtime", { paths: [__dirname] });
        }

        try {
            return originalResolveFilename(request, parent, isMain, options);
        } catch (error) {
            // If the original resolution fails and it's looking for a module,
            // try resolving from our node_modules
            if (
                error instanceof Error &&
                error.message.includes("Cannot find module")
            ) {
                try {
                    return require.resolve(request, { paths: [__dirname] });
                } catch {
                    // If that also fails, throw the original error
                    throw error;
                }
            }
            throw error;
        }
    };
}

const config: PlaywrightTestConfig = {
    testDir: "..",
    testMatch: "**/*.uitest.*",
    timeout: 30000,
    expect: {
        timeout: 5000,
    },
    use: {
        baseURL: "http://127.0.0.1:5173",
        trace: "on-first-retry",
    },
    // Spin up the Vite dev server so the harness is available during tests.
    webServer: {
        command: "yarn dev",
        cwd: __dirname,
        url: "http://127.0.0.1:5173",
        reuseExistingServer: true,
        stdout: "pipe",
        stderr: "pipe",
        timeout: 120_000,
    },
};

export default config;
