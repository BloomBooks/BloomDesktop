// The only module that may import @chenglou/pretext. Everything else works against the
// LineMeasurer interface, so tests can inject a measurer with no layout engine behind it.

import {
    layoutNextLine,
    prepareWithSegments,
    type LayoutCursor,
    type PreparedTextWithSegments,
} from "@chenglou/pretext";
import { BoxMetrics, LineMeasurer } from "./flowFit";

const kLineStartCursor: LayoutCursor = { segmentIndex: 0, graphemeIndex: 0 };

export function createPretextMeasurer(): LineMeasurer {
    return { measureFit: getTextThatFits };
}

let theMeasurer: LineMeasurer | undefined;

/** The one measurer the editing code shares. Made on first use, not at load time. */
export function getPretextMeasurer(): LineMeasurer {
    theMeasurer = theMeasurer ?? createPretextMeasurer();
    return theMeasurer;
}

/**
 * Lay the text out line by line at this width, and report the offset of the first character
 * that no longer has a line to sit on.
 */
function getTextThatFits(text: string, metrics: BoxMetrics): number {
    if (!text.length) {
        return 0;
    }

    const prepared = prepareWithSegments(text, metrics.font, {
        whiteSpace: "pre-wrap",
    });
    let cursor: LayoutCursor = kLineStartCursor;
    let usedHeight = 0;

    while (true) {
        const line = layoutNextLine(prepared, cursor, metrics.width);
        if (!line) {
            return text.length;
        }

        if (usedHeight + metrics.lineHeight > metrics.height) {
            return cursorToOffset(prepared, cursor);
        }

        usedHeight += metrics.lineHeight;
        cursor = line.end;
    }
}

/**
 * Pretext counts in segments and graphemes; the rest of the flow code counts in string
 * offsets, so that a Range can be put at the same place.
 */
function cursorToOffset(
    prepared: PreparedTextWithSegments,
    cursor: LayoutCursor,
): number {
    let offset = 0;
    for (let index = 0; index < cursor.segmentIndex; index++) {
        offset += prepared.segments[index].length;
    }

    if (
        cursor.segmentIndex >= prepared.segments.length ||
        cursor.graphemeIndex === 0
    ) {
        return offset;
    }

    const segment = prepared.segments[cursor.segmentIndex];
    let graphemeIndex = 0;
    for (const part of new Intl.Segmenter(undefined, {
        granularity: "grapheme",
    }).segment(segment)) {
        if (graphemeIndex >= cursor.graphemeIndex) {
            break;
        }

        offset += part.segment.length;
        graphemeIndex++;
    }

    return offset;
}
