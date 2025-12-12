/* eslint-env node */
/* global console, process */
import path from "path";
import { pathToFileURL, fileURLToPath } from "url";
import fs from "fs";
import { glob } from "glob";
import pug from "pug";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function resolvePaths(options = {}) {
    const browserUIRoot =
        options.browserUIRoot ?? path.resolve(__dirname, "..");
    const contentRoot =
        options.contentRoot ?? path.resolve(browserUIRoot, "../content");
    const outputBase =
        options.outputBase ??
        path.resolve(browserUIRoot, "../../output/browser");

    return { browserUIRoot, contentRoot, outputBase };
}

export async function compilePugFiles(options = {}) {
    const { browserUIRoot, contentRoot, outputBase } = resolvePaths(options);

    const browserUIPugFiles = glob.sync("**/*.pug", {
        cwd: browserUIRoot,
        ignore: ["**/node_modules/**", "**/*mixins.pug"],
        nodir: true,
        absolute: true,
    });

    const contentPugFiles = glob.sync("**/*.pug", {
        cwd: contentRoot,
        ignore: ["**/node_modules/**", "**/*mixins.pug"],
        nodir: true,
        absolute: true,
    });

    const allPugFiles = [...browserUIPugFiles, ...contentPugFiles];

    console.log(
        `\nCompiling ${allPugFiles.length} Pug files (${browserUIPugFiles.length} from BloomBrowserUI, ${contentPugFiles.length} from content)...`,
    );

    for (const file of allPugFiles) {
        const isContentFile = file.startsWith(contentRoot + path.sep);
        const baseRoot = isContentFile ? contentRoot : browserUIRoot;
        const relativePath = path
            .relative(baseRoot, file)
            .replace(/\\/g, "/")
            .replace(/\.pug$/i, ".html");

        const outputFile = path.join(outputBase, relativePath);
        const outputDir = path.dirname(outputFile);

        if (!fs.existsSync(outputDir)) {
            fs.mkdirSync(outputDir, { recursive: true });
        }

        const html = pug.renderFile(file, {
            basedir: baseRoot,
            pretty: true,
        });

        fs.writeFileSync(outputFile, html);
        const displayPath = path.relative(browserUIRoot, file);
        console.log(`  ✓ ${displayPath} → ${relativePath}`);
    }

    console.log("Pug compilation complete!\n");
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
    compilePugFiles().catch((err) => {
        console.error("Failed to compile Pug files:", err);
        process.exitCode = 1;
    });
}
