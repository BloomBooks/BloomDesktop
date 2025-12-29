const chokidar = require("chokidar");
const fs = require("fs");
const path = require("path");
const { glob } = require("glob");
const less = require("less");

const isWindows = process.platform === "win32";
const defaultIgnore = ["**/node_modules/**"];

function scanLessImports(sourceText) {
    // Capture common LESS import forms:
    //   @import "a.less";
    //   @import (reference) "a.less";
    //   @import url("a.less");
    //   @import (reference) url("a.less");
    // We intentionally ignore variable-based/dynamic import paths.
    const imports = [];
    const importRegex =
        /@import\s*(?:\([^)]+\)\s*)?(?:url\(\s*)?["']([^"']+)["']\s*\)?\s*;?/gi;
    let match;
    while ((match = importRegex.exec(sourceText))) {
        const raw = (match[1] ?? "").trim();
        if (!raw) {
            continue;
        }
        if (
            raw.includes("@{") ||
            raw.includes("://") ||
            raw.startsWith("data:")
        ) {
            continue;
        }
        imports.push(raw);
    }
    return imports;
}

function resolveLessImport(fromFilePath, importPath) {
    if (!importPath || typeof importPath !== "string") {
        return null;
    }

    // Ignore module-style imports (e.g. ~package/path.less). If these become relevant,
    // prefer relying on actual less compilation to discover them.
    if (importPath.startsWith("~")) {
        return null;
    }

    const baseDir = path.dirname(fromFilePath);
    const candidate = path.isAbsolute(importPath)
        ? importPath
        : path.resolve(baseDir, importPath);

    const ext = path.extname(candidate);
    const candidates = [];
    if (ext) {
        candidates.push(candidate);
    } else {
        candidates.push(`${candidate}.less`);
        candidates.push(candidate);
    }

    for (const filePath of candidates) {
        try {
            if (fs.statSync(filePath).isFile()) {
                return path.resolve(filePath);
            }
        } catch {
            // try next candidate
        }
    }

    return null;
}

function normalizePath(filePath) {
    const resolved = path.resolve(filePath);
    return isWindows ? resolved.toLowerCase() : resolved;
}

function ensureDir(dirPath) {
    if (!fs.existsSync(dirPath)) {
        fs.mkdirSync(dirPath, { recursive: true });
    }
}

function readJson(filePath) {
    try {
        return JSON.parse(fs.readFileSync(filePath, "utf8"));
    } catch {
        return null;
    }
}

function writeJsonAtomic(filePath, data) {
    ensureDir(path.dirname(filePath));
    const tmpPath = `${filePath}.tmp`;
    fs.writeFileSync(tmpPath, JSON.stringify(data, null, 2));
    fs.renameSync(tmpPath, filePath);
}

function pathToRepoRelative(repoRoot, absPath) {
    return path.relative(repoRoot, absPath).replace(/\\/g, "/");
}

function repoRelativeToAbsolute(repoRoot, relativePath) {
    return path.resolve(repoRoot, relativePath);
}

function toArray(value, fallback) {
    if (!value) {
        return fallback.slice();
    }
    return Array.isArray(value) ? value.slice() : [value];
}

class LessWatchManager {
    constructor(options) {
        this.repoRoot = options.repoRoot;
        this.metadataPath = options.metadataPath;
        this.targets = options.targets.map((target) => ({
            ...target,
            root: path.resolve(target.root),
            outputBase: path.resolve(target.outputBase),
            include: toArray(target.include, ["**/*.less"]),
            ignore: toArray(target.ignore, defaultIgnore),
            explicitEntries: target.entries?.map((entry) =>
                path.resolve(target.root, entry),
            ),
        }));
        this.logger = options.logger ?? console;
        this.lessRenderer = options.lessRenderer ?? less.render;
        this.metadataVersion = 1;

        this.entries = new Map();
        this.entryByPath = new Map();
        this.entryDependencies = new Map();
        this.dependencyToEntries = new Map();
        this.pendingBuilds = new Map();
        this.watchers = [];
        this.dependencyWatcher = null;
    }

    async initialize() {
        await this.loadMetadata();
        await this.registerInitialEntries();
        await this.ensureEntryDependenciesKnown();
        await this.ensureOutputsUpToDate();
    }

    async ensureEntryDependenciesKnown() {
        // The watcher relies on the dependency graph to know what to rebuild.
        // If we have outputs already (built by some other pipeline) and no metadata yet,
        // we still need a best-effort dependency graph so changes rebuild dependents.
        for (const entry of this.entries.values()) {
            const deps = this.entryDependencies.get(entry.id);
            if (deps && deps.length > 0) {
                continue;
            }

            const lessInput = fs.readFileSync(entry.entryPath, "utf8");
            const imports = scanLessImports(lessInput)
                .map((importPath) =>
                    resolveLessImport(entry.entryPath, importPath),
                )
                .filter((dep) => !!dep);

            const dependencies = [path.resolve(entry.entryPath), ...imports];
            this.updateEntryDependencies(entry.id, dependencies);
        }
    }

    async startWatching() {
        if (this.watchers.length > 0) {
            return;
        }

        const failFast = (promise) =>
            promise.catch((err) => {
                this.logger.error(`[LESS] watcher failure:`, err);
                process.exit(1);
            });

        for (const target of this.targets) {
            const watcher = chokidar.watch(target.include, {
                cwd: target.root,
                ignored: target.ignore,
                ignoreInitial: true,
                awaitWriteFinish: {
                    stabilityThreshold: 200,
                    pollInterval: 50,
                },
            });

            watcher
                .on("add", (file) =>
                    failFast(
                        this.handleFileAdded(
                            target,
                            path.join(target.root, file),
                        ),
                    ),
                )
                .on("change", (file) =>
                    failFast(
                        this.handleFileChanged(
                            path.join(target.root, file),
                            `modified ${path.basename(file)}`,
                        ),
                    ),
                )
                .on("unlink", (file) =>
                    failFast(
                        this.handleFileRemoved(path.join(target.root, file)),
                    ),
                );

            this.watchers.push(watcher);
        }

        this.dependencyWatcher = chokidar.watch([], {
            ignoreInitial: true,
            awaitWriteFinish: {
                stabilityThreshold: 200,
                pollInterval: 50,
            },
        });

        this.dependencyWatcher
            .on("change", (file) =>
                failFast(this.handleFileChanged(file, "dependency changed")),
            )
            .on("unlink", (file) => failFast(this.handleFileRemoved(file)));

        this.primeDependencyWatcher();
    }

    async dispose() {
        for (const watcher of this.watchers) {
            await watcher.close();
        }
        this.watchers = [];
        if (this.dependencyWatcher) {
            await this.dependencyWatcher.close();
            this.dependencyWatcher = null;
        }
    }

    async loadMetadata() {
        const data = readJson(this.metadataPath);
        if (!data || data.version !== this.metadataVersion) {
            return;
        }

        for (const [entryId, deps] of Object.entries(data.entries ?? {})) {
            const absDeps = deps.map((dep) =>
                repoRelativeToAbsolute(this.repoRoot, dep),
            );
            this.entryDependencies.set(entryId, absDeps);
            for (const dep of absDeps) {
                this.linkDependency(entryId, dep, { watch: false });
            }
        }
    }

    async registerInitialEntries() {
        for (const target of this.targets) {
            const files = target.explicitEntries
                ? target.explicitEntries
                : await this.globEntries(target);
            for (const file of files) {
                this.registerEntry(file, target);
            }
        }

        for (const entryId of Array.from(this.entryDependencies.keys())) {
            if (!this.entries.has(entryId)) {
                this.clearDependencyMappings(entryId);
                this.entryDependencies.delete(entryId);
            }
        }
    }

    async globEntries(target) {
        const results = new Set();
        for (const pattern of target.include) {
            const files = await glob(pattern, {
                cwd: target.root,
                ignore: target.ignore,
                nodir: true,
                absolute: true,
            });
            for (const file of files) {
                results.add(path.resolve(file));
            }
        }
        return Array.from(results);
    }

    registerEntry(entryPath, target) {
        const absPath = path.resolve(entryPath);
        const key = pathToRepoRelative(this.repoRoot, absPath);
        if (this.entries.has(key)) {
            return this.entries.get(key);
        }

        const relativeToRoot = path.relative(target.root, absPath);
        const outputPath = path.join(
            target.outputBase,
            relativeToRoot.replace(/\.less$/i, ".css"),
        );

        const entry = {
            id: key,
            entryPath: absPath,
            outputPath,
            target,
        };

        this.entries.set(key, entry);
        this.entryByPath.set(normalizePath(absPath), key);
        return entry;
    }

    async ensureOutputsUpToDate() {
        for (const entry of this.entries.values()) {
            if (await this.needsBuild(entry)) {
                await this.compileEntry(entry, "initial sync");
            }
        }
        await this.persistMetadata();
    }

    async needsBuild(entry) {
        const outputMTime = this.getMTime(entry.outputPath);
        if (!outputMTime) {
            return true;
        }

        const deps = this.entryDependencies.get(entry.id);
        if (!deps || deps.length === 0) {
            const sourceTime = this.getMTime(entry.entryPath);
            return !sourceTime || sourceTime > outputMTime;
        }

        for (const dep of deps) {
            const depTime = this.getMTime(dep);
            if (!depTime || depTime > outputMTime) {
                return true;
            }
        }
        return false;
    }

    getMTime(filePath) {
        try {
            return fs.statSync(filePath).mtimeMs;
        } catch {
            return 0;
        }
    }

    async handleFileAdded(target, absPath) {
        this.registerEntry(absPath, target);
        await this.queueBuildForPath(absPath, "file added");
    }

    async handleFileChanged(absPath, reason) {
        const affected = this.collectAffectedEntries(absPath);
        await Promise.all(
            Array.from(affected).map((entryId) =>
                this.queueBuild(entryId, reason),
            ),
        );
    }

    async handleFileRemoved(absPath) {
        const affected = this.collectAffectedEntries(absPath);
        const key = normalizePath(absPath);
        const entryId = this.entryByPath.get(key);
        if (entryId) {
            await this.removeEntry(entryId);
        }

        await Promise.all(
            Array.from(affected).map((affectedEntryId) =>
                this.queueBuild(affectedEntryId, "dependency removed"),
            ),
        );
    }

    collectAffectedEntries(absPath) {
        // Rebuild entries that directly include the changed file, and also rebuild any entries
        // that depend on those entries (transitively).
        // Example: bloomWebFonts.less -> bloomUI.less -> editMode.less
        const startKey = normalizePath(absPath);
        const affected = new Set();
        const visitedFileKeys = new Set([startKey]);
        const queue = [startKey];

        while (queue.length > 0) {
            const fileKey = queue.shift();

            const directEntryId = this.entryByPath.get(fileKey);
            if (directEntryId) {
                affected.add(directEntryId);
            }

            const dependents = this.dependencyToEntries.get(fileKey);
            if (!dependents) {
                continue;
            }

            for (const dependentEntryId of dependents) {
                if (!this.entries.has(dependentEntryId)) {
                    continue;
                }
                affected.add(dependentEntryId);

                const dependentEntry = this.entries.get(dependentEntryId);
                if (!dependentEntry) {
                    continue;
                }
                const dependentEntryPathKey = normalizePath(
                    dependentEntry.entryPath,
                );
                if (!visitedFileKeys.has(dependentEntryPathKey)) {
                    visitedFileKeys.add(dependentEntryPathKey);
                    queue.push(dependentEntryPathKey);
                }
            }
        }

        return affected;
    }

    async queueBuildForPath(absPath, reason) {
        const key = normalizePath(absPath);
        const entryId = this.entryByPath.get(key);
        if (!entryId) {
            return;
        }
        await this.queueBuild(entryId, reason);
    }

    async queueBuild(entryId, reason) {
        if (!this.entries.has(entryId)) {
            return;
        }
        const pending = this.pendingBuilds.get(entryId) ?? Promise.resolve();
        const next = pending
            .catch(() => {})
            .then(() => this.compileEntry(this.entries.get(entryId), reason));

        this.pendingBuilds.set(
            entryId,
            next.finally(() => {
                if (this.pendingBuilds.get(entryId) === next) {
                    this.pendingBuilds.delete(entryId);
                }
            }),
        );

        await next;
    }

    async compileEntry(entry, reason) {
        ensureDir(path.dirname(entry.outputPath));
        const lessInput = fs.readFileSync(entry.entryPath, "utf8");
        const result = await this.lessRenderer(lessInput, {
            filename: entry.entryPath,
            sourceMap: {
                sourceMapFileInline: false,
                outputSourceFiles: true,
                sourceMapURL: `${path.basename(entry.outputPath)}.map`,
            },
        });

        let css = result.css;
        if (result.map) {
            css += `\n/*# sourceMappingURL=${path.basename(entry.outputPath)}.map */`;
            fs.writeFileSync(`${entry.outputPath}.map`, result.map);
        }
        fs.writeFileSync(entry.outputPath, css);

        const dependencies = (result.imports ?? []).map((dep) =>
            path.resolve(dep),
        );
        if (!dependencies.includes(path.resolve(entry.entryPath))) {
            dependencies.unshift(path.resolve(entry.entryPath));
        }

        this.updateEntryDependencies(entry.id, dependencies);
        await this.persistMetadata();

        this.logger.log(
            `[LESS] ✓ ${entry.id} (${reason ?? "recompiled"}) → ${pathToRepoRelative(
                this.repoRoot,
                entry.outputPath,
            )}`,
        );
    }

    clearDependencyMappings(entryId) {
        const prevDeps = this.entryDependencies.get(entryId) ?? [];
        for (const dep of prevDeps) {
            const key = normalizePath(dep);
            const set = this.dependencyToEntries.get(key);
            if (set) {
                set.delete(entryId);
                if (set.size === 0) {
                    this.dependencyToEntries.delete(key);
                    if (this.dependencyWatcher) {
                        this.dependencyWatcher.unwatch(dep);
                    }
                }
            }
        }
    }

    linkDependency(entryId, dep, options = {}) {
        const abs = path.resolve(dep);
        const key = normalizePath(abs);
        let set = this.dependencyToEntries.get(key);
        if (!set) {
            set = new Set();
            this.dependencyToEntries.set(key, set);
        }
        set.add(entryId);

        const shouldWatch = options.watch ?? false;
        if (
            shouldWatch &&
            this.dependencyWatcher &&
            !this.isInsideKnownTarget(abs)
        ) {
            this.dependencyWatcher.add(abs);
        }
        return abs;
    }

    updateEntryDependencies(entryId, dependencies) {
        this.clearDependencyMappings(entryId);

        const uniqueDeps = [];
        const seen = new Set();
        for (const dep of dependencies) {
            const abs = path.resolve(dep);
            const key = normalizePath(abs);
            if (seen.has(key)) {
                continue;
            }
            seen.add(key);
            uniqueDeps.push(abs);
            this.linkDependency(entryId, abs, { watch: true });
        }

        this.entryDependencies.set(entryId, uniqueDeps);
    }

    isInsideKnownTarget(filePath) {
        const absPath = path.resolve(filePath);
        return this.targets.some(
            (target) =>
                absPath === target.root ||
                absPath.startsWith(`${target.root}${path.sep}`),
        );
    }

    async removeEntry(entryId) {
        const entry = this.entries.get(entryId);
        if (!entry) {
            return;
        }
        this.entries.delete(entryId);
        this.entryByPath.delete(normalizePath(entry.entryPath));
        this.clearDependencyMappings(entryId);
        this.entryDependencies.delete(entryId);
        if (fs.existsSync(entry.outputPath)) {
            fs.unlinkSync(entry.outputPath);
        }
        if (fs.existsSync(`${entry.outputPath}.map`)) {
            fs.unlinkSync(`${entry.outputPath}.map`);
        }
        await this.persistMetadata();
        this.logger.warn(`[LESS] removed entry ${entryId}`);
    }

    async persistMetadata() {
        const entries = {};
        for (const [entryId, deps] of this.entryDependencies.entries()) {
            entries[entryId] = deps.map((dep) =>
                path.relative(this.repoRoot, dep).replace(/\\/g, "/"),
            );
        }
        writeJsonAtomic(this.metadataPath, {
            version: this.metadataVersion,
            entries,
        });
    }

    primeDependencyWatcher() {
        if (!this.dependencyWatcher) {
            return;
        }

        for (const deps of this.entryDependencies.values()) {
            for (const dep of deps ?? []) {
                if (!this.isInsideKnownTarget(dep)) {
                    this.dependencyWatcher.add(dep);
                }
            }
        }
    }
}

module.exports = { LessWatchManager };
