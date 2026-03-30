import { writeFileSync, readFileSync } from "fs";

// This file implements the package.json command build:pageSizes, which creates
// bookLayout/page-size-mixin.less and DistFiles/pageSizesLookup.json from DistFiles/pageSizes.json.
interface PageSize {
    size: string;
    width: string;
    height: string;
}

interface PageSizesFile {
    sizes: PageSize[];
}

interface PageSizeLookupEntry {
    width: number;
    height: number;
}

interface PageSizeLookupFile {
    sizes: Record<string, PageSizeLookupEntry>;
}

const sourcePath = "../../DistFiles/pageSizes.json";
const lessOutputPath = "bookLayout/page-size-mixin.less";
const lookupOutputPath = "../../DistFiles/pageSizesLookup.json";
const inchesToMillimeters = 25.4;

/**
 * Reads the canonical page size list from DistFiles.
 */
function readConfiguredPageSizes(): PageSize[] {
    const input = readFileSync(sourcePath, "utf8");
    const parsed = JSON.parse(input) as PageSizesFile;
    return parsed.sizes;
}

/**
 * Converts a CSS-style dimension string (e.g. "148mm", "8.5in") to millimeters.
 */
function convertDimensionToMillimeters(value: string): number {
    if (value.endsWith("mm")) {
        return Number.parseFloat(value.substring(0, value.length - 2));
    }

    if (value.endsWith("in")) {
        const inches = Number.parseFloat(value.substring(0, value.length - 2));
        return inches * inchesToMillimeters;
    }

    throw new Error(`Unsupported page-size unit in '${value}'.`);
}

/**
 * Returns true when the layout is a printable paper layout (not a device/story preset).
 */
function isPaperLayout(sizeName: string): boolean {
    return (
        !sizeName.startsWith("Device") && !sizeName.startsWith("PictureStory")
    );
}

/**
 * Returns the base paper name for oriented sizes (e.g. A4Portrait -> A4).
 */
function getBaseSizeName(sizeName: string): string | undefined {
    for (const suffix of ["Portrait", "Landscape"]) {
        if (sizeName.endsWith(suffix)) {
            return sizeName.slice(0, -suffix.length);
        }
    }

    return undefined;
}

/**
 * Builds the less variable content consumed by the layout stylesheets.
 * Each size produces @{size}-Height and @{size}-Width variables.
 */
function buildLessPageSizeVariables(configuredSizes: PageSize[]): string {
    let data = "";
    for (const pageSize of configuredSizes) {
        data += `@${pageSize.size}-Height: ${pageSize.height};\n`;
        data += `@${pageSize.size}-Width: ${pageSize.width};\n`;
    }

    return data;
}

/**
 * Adds configured page sizes and oriented aliases (A4Portrait/A4Landscape => A4) to the lookup.
 * If both orientations exist, Portrait is preferred for the base key (e.g. A4).
 */
function addConfiguredSizesToLookup(
    configuredSizes: PageSize[],
    lookup: Record<string, PageSizeLookupEntry>,
): void {
    for (const item of configuredSizes) {
        if (!isPaperLayout(item.size)) {
            continue;
        }

        const width = convertDimensionToMillimeters(item.width);
        const height = convertDimensionToMillimeters(item.height);
        lookup[item.size] = { width, height };

        const baseName = getBaseSizeName(item.size);
        if (!baseName) {
            continue;
        }

        const shouldPreferThisAsBase =
            item.size.endsWith("Portrait") || lookup[baseName] === undefined;
        if (shouldPreferThisAsBase) {
            lookup[baseName] = { width, height };
        }
    }
}

/**
 * Adds ISO A/B series sizes (A0-A10, B0-B10) to the lookup.
 */
function addIsoSeriesSizes(lookup: Record<string, PageSizeLookupEntry>): void {
    addIsoSeries(lookup, "A", 841, 1189);
    addIsoSeries(lookup, "B", 1000, 1414);
}

/**
 * Adds one ISO series by repeatedly halving the long edge and rotating.
 */
function addIsoSeries(
    lookup: Record<string, PageSizeLookupEntry>,
    series: string,
    startWidth: number,
    startHeight: number,
): void {
    let width = startWidth;
    let height = startHeight;
    for (let index = 0; index <= 10; index++) {
        const key = `${series}${index}`;
        if (lookup[key] === undefined) {
            lookup[key] = { width, height };
        }

        const nextWidth = Math.floor(height / 2);
        height = width;
        width = nextWidth;
    }
}

/**
 * Adds common square aliases like Cm13/In8 so runtime logic can use data lookups.
 * Covers Cm1-Cm50 and In1-In20.
 */
function addSquareAliases(lookup: Record<string, PageSizeLookupEntry>): void {
    for (let centimeters = 1; centimeters <= 50; centimeters++) {
        const key = `Cm${centimeters}`;
        if (lookup[key] === undefined) {
            const side = centimeters * 10;
            lookup[key] = { width: side, height: side };
        }
    }

    for (let inches = 1; inches <= 20; inches++) {
        const key = `In${inches}`;
        if (lookup[key] === undefined) {
            const side = inches * inchesToMillimeters;
            lookup[key] = { width: side, height: side };
        }
    }
}

/**
 * Creates the mm lookup file used by C# runtime code.
 */
function buildLookupFile(configuredSizes: PageSize[]): PageSizeLookupFile {
    const lookup: Record<string, PageSizeLookupEntry> = {};
    addConfiguredSizesToLookup(configuredSizes, lookup);
    addIsoSeriesSizes(lookup);
    addSquareAliases(lookup);
    return { sizes: lookup };
}

/**
 * Generates both build artifacts derived from pageSizes.json.
 */
function main(): void {
    const configuredSizes = readConfiguredPageSizes();

    // Output 1: less variables used by page layout stylesheets.
    writeFileSync(lessOutputPath, buildLessPageSizeVariables(configuredSizes));

    // Output 2: mm lookup used by C# runtime page-size code.
    const lookup = buildLookupFile(configuredSizes);
    writeFileSync(lookupOutputPath, JSON.stringify(lookup, null, 4) + "\n");
}

main();
