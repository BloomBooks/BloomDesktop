// The folio scale check: a folio of 40 books of 8 pages, each page with a large photograph, made
// into a PDF, timing how long that takes and recording the most memory Bloom and its PDF maker
// used. Run it by hand against a Release build of Bloom (customers run Release):
//
//   dotnet build src/BloomExe/BloomExe.csproj -c Release
//   pnpm exec playwright test --config scripts/playwright.config.ts folio-scale
//
// It prints its measurements; it asserts nothing about them, because what counts as fast enough
// is a judgment about the numbers, not a threshold a machine can hold.

import { execFileSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { test } from "../fixtures/bloomTest";
import { enableFlowTextFeature, kFlowTextFeatures } from "../helpers/flowText";
import { selectBook } from "../helpers/collection";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    findBookFolder,
} from "../helpers/bookMaking";
import { makeFolio, readBookId, setFolioBooks } from "../helpers/folio";
import { chooseImageFile } from "../helpers/images";
import { readPdf } from "../helpers/pdf";
import { makePdfInPublishTab } from "../helpers/pdfPublish";
import { switchTab } from "../helpers/workspace";

const kBookCount = 40;
const kPagesPerBook = 8;

// Folio needs a Pro subscription and, until flow text is ready, the flow-text experimental feature.
// The collection is given the Pro tier after each launch (enableFlowTextFeature).
test.use({
    collectionSpec: {
        name: "folio-scale",
        languages: ["en"],
    },
    experimentalFeatures: kFlowTextFeatures,
});

/** A 3000 × 2000 photograph-like JPEG, which is what makes a PDF heavy. */
function makeLargeImage(): string {
    const folder = fs.mkdtempSync(Path.join(os.tmpdir(), "folio-scale-"));
    const path = Path.join(folder, "large photo.jpg");
    execFileSync("py", [
        "-c",
        "import sys,random\n" +
            "from PIL import Image\n" +
            "w,h=3000,2000\n" +
            "im=Image.effect_noise((w,h),64).convert('RGB')\n" +
            "im.save(sys.argv[1],quality=90)",
        path,
    ]);
    return path;
}

/**
 * Working sets in MB, keyed by process name: the Bloom with this process id, and the largest
 * process of each of the other names. Other Blooms may be running, so Bloom goes by its id; the
 * PDF maker and Ghostscript run only while a PDF is being made.
 */
function workingSetsMb(
    bloomPid: number,
    names: string[],
): Record<string, number> {
    const output = execFileSync(
        "powershell",
        [
            "-NoProfile",
            "-Command",
            `@(Get-Process -Id ${bloomPid}) + @(Get-Process -Name ${names.join(",")} -ErrorAction SilentlyContinue) | ForEach-Object { "$($_.ProcessName) $($_.WorkingSet64)" }`,
        ],
        { encoding: "utf8" },
    );
    const result: Record<string, number> = {};
    for (const line of output.split(/\r?\n/).filter((l) => l.trim())) {
        const [name, bytes] = line.trim().split(" ");
        result[name] = Math.max(result[name] ?? 0, Number(bytes) / 1024 / 1024);
    }
    return result;
}

/**
 * Give every copied book ("Scale Book 2" on) new photographs of its own, under the same file names,
 * in one Python process.
 */
function replacePhotosInCopies(collectionDir: string) {
    const paths = fs
        .readdirSync(collectionDir)
        .filter((name) => /^Scale Book \d+$/.test(name))
        .flatMap((name) => {
            const folder = Path.join(collectionDir, name);
            return fs
                .readdirSync(folder)
                .filter((f) => /\.jpe?g$/i.test(f))
                .map((f) => Path.join(folder, f));
        });
    const listPath = Path.join(
        os.tmpdir(),
        `folio-scale-photos-${process.pid}.txt`,
    );
    fs.writeFileSync(listPath, paths.join("\n"));
    execFileSync("py", [
        "-c",
        "import sys\n" +
            "from PIL import Image\n" +
            "for p in open(sys.argv[1],encoding='utf-8').read().splitlines():\n" +
            "    Image.effect_noise((3000,2000),64).convert('RGB').save(p,quality=90)",
        listPath,
    ]);
    fs.rmSync(listPath);
}

test("make a PDF of a folio of 40 books with large photographs", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(3 * 60 * 60 * 1000);
    const image = makeLargeImage();

    // One book, made the ordinary way, then copied on disk into the rest.
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Scale Book");
    for (let i = 0; i < kPagesPerBook; i++) {
        await addPage(page, "Basic Text & Image", 1, "Basic Book");
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[contentPages.length - 1].id);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            `Page ${i + 1}.`,
        );
        await chooseImageFile(page, image);
    }
    await goToPage(page, (await getPages(page))[0].id);
    await switchTab(page, "collection");
    const firstBook = await findBookFolder(page, "Scale Book");
    const folio = await makeFolio(page, "Scale Folio");

    const ids = [readBookId(firstBook)];
    await bloomApp.restart(() => {
        for (let n = 2; n <= kBookCount; n++) {
            const folder = Path.join(bloomApp.collectionDir, `Scale Book ${n}`);
            fs.cpSync(firstBook, folder, { recursive: true });
            fs.renameSync(
                Path.join(folder, `${Path.basename(firstBook)}.htm`),
                Path.join(folder, `Scale Book ${n}.htm`),
            );
            const metaPath = Path.join(folder, "meta.json");
            const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
            meta.bookInstanceId = crypto.randomUUID();
            fs.writeFileSync(metaPath, JSON.stringify(meta));
            ids.push(meta.bookInstanceId);
        }
        // Real books have their own pictures. Were every copy to share the same few, the PDF would
        // store each only once, and look far smaller and cheaper to make than a real folio.
        replacePhotosInCopies(bloomApp.collectionDir);
        setFolioBooks(folio, ids);
    });
    const shell = bloomApp.page;
    await enableFlowTextFeature(shell);

    await selectBook(shell, folio);
    // Every request to make a PDF, with when it was made, so a second one would show.
    const pdfRequests: string[] = [];
    shell.on("request", (request) => {
        if (/publish\/pdf\/(simple|pages|cover)/.test(request.url()))
            pdfRequests.push(`${new Date().toISOString()} ${request.url()}`);
    });
    const names = ["BloomPdfMaker", "gswin32c"];
    const peaks: Record<string, number> = {};
    const sampler = setInterval(() => {
        for (const [name, mb] of Object.entries(
            workingSetsMb(bloomApp.bloomPid, names),
        ))
            peaks[name] = Math.max(peaks[name] ?? 0, mb);
    }, 1000);
    const start = Date.now();
    let pdfPath: string;
    try {
        pdfPath = await makePdfInPublishTab(
            shell,
            "Simple",
            2 * 60 * 60 * 1000,
        );
    } finally {
        clearInterval(sampler);
    }
    const seconds = (Date.now() - start) / 1000;
    console.log(`PDF requests made:\n  ${pdfRequests.join("\n  ")}`);
    const pages = readPdf(pdfPath);
    const sizeMb = fs.statSync(pdfPath).size / 1024 / 1024;
    console.log(
        `FOLIO SCALE: ${kBookCount} books, ${pages.length} PDF pages, ${sizeMb.toFixed(1)} MB PDF, ` +
            `${seconds.toFixed(0)} s. Peak working set (MB, sampled each second): ` +
            Object.entries(peaks)
                .map(([n, mb]) => `${n} ${mb.toFixed(0)}`)
                .join(", "),
    );
});
