import type { RenderOutput } from "less";

export interface LessWatchTarget {
    name: string;
    root: string;
    outputBase: string;
    include?: string | string[];
    ignore?: string | string[];
    entries?: string[];
}

export interface LessWatchOptions {
    repoRoot: string;
    metadataPath: string;
    targets: LessWatchTarget[];
    logger?: Console;
    lessRenderer?: (
        input: string,
        options: Record<string, unknown>,
    ) => Promise<RenderOutput>;
}

export class LessWatchManager {
    constructor(options: LessWatchOptions);
    initialize(): Promise<void>;
    startWatching(): Promise<void>;
    dispose(): Promise<void>;
    handleFileAdded(target: LessWatchTarget, absPath: string): Promise<void>;
    handleFileChanged(absPath: string, reason: string): Promise<void>;
    handleFileRemoved(absPath: string): Promise<void>;
    entryDependencies: Map<string, string[]>;
    targets: LessWatchTarget[];
    repoRoot: string;
}
