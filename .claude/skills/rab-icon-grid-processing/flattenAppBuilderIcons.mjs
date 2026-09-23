import { promises as fs } from "node:fs";
import path from "node:path";

const workspaceRoot = process.cwd();
const iconRoot = path.join(workspaceRoot, "DistFiles", "appbuilder-icons");

const main = async () => {
    const rootEntries = await fs.readdir(iconRoot, { withFileTypes: true });
    let movedCount = 0;
    let removedCount = 0;
    const plannedMoves = [];
    const rootFileNames = new Set(
        rootEntries
            .filter((entry) => entry.isFile())
            .map((entry) => entry.name),
    );
    const plannedDestinationNames = new Set(rootFileNames);

    for (const entry of rootEntries) {
        if (!entry.isDirectory()) {
            continue;
        }

        const directoryPath = path.join(iconRoot, entry.name);
        const childEntries = await fs.readdir(directoryPath, {
            withFileTypes: true,
        });

        for (const childEntry of childEntries) {
            if (!childEntry.isFile()) {
                continue;
            }

            const sourcePath = path.join(directoryPath, childEntry.name);
            if (plannedDestinationNames.has(childEntry.name)) {
                throw new Error(
                    `Cannot flatten icons because ${childEntry.name} already exists at the root.`,
                );
            }

            plannedDestinationNames.add(childEntry.name);
            plannedMoves.push({
                sourcePath,
                destinationPath: path.join(iconRoot, childEntry.name),
            });
        }
    }

    for (const move of plannedMoves) {
        await fs.rename(move.sourcePath, move.destinationPath);
        movedCount += 1;
    }

    for (const entry of rootEntries) {
        if (!entry.isDirectory()) {
            continue;
        }

        const directoryPath = path.join(iconRoot, entry.name);
        await fs.rmdir(directoryPath);
        removedCount += 1;
    }

    console.log(
        `Flattened ${movedCount} files from ${removedCount} folders under ${workspaceRoot}.`,
    );
};

await main();
