import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { getDefaultRepoRoot } from "./bloomProcessCommon.mjs";

const args = process.argv.slice(2);
const watch = args.includes("--watch");
const repoRootArgIndex = args.indexOf("--repo-root");
const repoRoot =
    repoRootArgIndex >= 0 ? args[repoRootArgIndex + 1] : getDefaultRepoRoot();
const projectPath = path.join(repoRoot, "src", "BloomExe", "BloomExe.csproj");

if (!existsSync(projectPath)) {
    console.error(
        `Bloom project not found at ${projectPath}. Verify --repo-root or run the command from this worktree.`,
    );
    process.exit(1);
}

const dotnetArgs = watch
    ? ["watch", "run", "--project", projectPath]
    : ["run", "--project", projectPath];

const child = spawn("dotnet", dotnetArgs, {
    stdio: "inherit",
    shell: false,
});

child.on("error", (error) => {
    console.error(`Failed to start dotnet: ${error.message}`);
    process.exit(1);
});

child.on("exit", (code, signal) => {
    if (signal) {
        process.kill(process.pid, signal);
        return;
    }

    process.exit(code ?? 0);
});
