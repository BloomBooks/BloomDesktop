// Absolute paths to the repo, the built Bloom.exe, and shared tool locations used across the
// E2E harness.
import * as path from "node:path";

// This file lives at src/BloomTests/e2e/harness/paths.ts — repo root is four levels up.
export const repoRoot = path.resolve(__dirname, "..", "..", "..", "..");

export const bloomExePath = path.join(
    repoRoot,
    "output",
    "Debug",
    "AnyCPU",
    "Bloom.exe",
);

export const bloomExeCsproj = path.join(
    repoRoot,
    "src",
    "BloomExe",
    "BloomExe.csproj",
);

// Skill helper scripts this harness reuses for process discovery/kill (see
// .github/skills/bloom-automation/SKILL.md) rather than reimplementing wmic/taskkill logic.
export const bloomAutomationSkillDir = path.join(
    repoRoot,
    ".github",
    "skills",
    "bloom-automation",
);
