import { promises as fs } from "node:fs";
import path from "node:path";

const workspaceRoot = process.cwd();
const iconRoot = path.join(workspaceRoot, "DistFiles", "appbuilder-icons");

const main = async () => {
    const rootEntries = await fs.readdir(iconRoot, { withFileTypes: true });
    let movedCount = 0;
    let removedCount = 0;

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
            const destinationPath = path.join(iconRoot, childEntry.name);

            await fs.rename(sourcePath, destinationPath);
            movedCount += 1;
        }

        await fs.rmdir(directoryPath);
        removedCount += 1;
    }

    console.log(
        `Flattened ${movedCount} files from ${removedCount} folders under ${workspaceRoot}.`,
    );
};

await main();
