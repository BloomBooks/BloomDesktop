// Utilities used by the Toolbox bundle that parse/interpret CSS values used for
// canvas element positioning/sizing.
//
// Keep this file dependency-light: it is intentionally safe to import from anywhere
// in the Toolbox without dragging in editView/CanvasElementManager.

// Parses CSS dimensions like "12px" (and plain numeric strings) into numbers.
export const pxToNumber = (
    cssDimension: string | undefined | null,
    fallback: number = NaN,
): number => {
    if (!cssDimension) {
        return 0;
    }

    const trimmed = cssDimension.trim();
    if (trimmed.endsWith("px")) {
        return parseFloat(trimmed.slice(0, -2));
    }

    // Some callers provide a numeric default like "0".
    if (/^-?\d+(\.\d+)?$/.test(trimmed)) {
        return parseFloat(trimmed);
    }

    return fallback;
};
