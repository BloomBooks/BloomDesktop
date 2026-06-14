// Cross-frame bridge from the Toolbox to the page-frame bloom-table operations.
//
// The toolbox and the editable page are separate iframes/JS realms, each with
// its own copy of the bloom-table module and its own tableHistoryManager
// singleton. Tables are attached in the PAGE frame, so structural ops only take
// effect when run through the page frame's module. This bridge fetches the
// TableApi object the page frame built (via editablePageBundle.getTableApi),
// so the toolbox-hosted TableMenu mutates tables in the realm that owns them.
//
// `import type` keeps the bloom-table implementation out of the toolbox bundle;
// only the type is referenced here, and it is erased at compile time.

import { getEditablePageBundleExports } from "../../js/workspaceFrames";
import type { TableApi } from "bloom-table";

/** The page-frame bloom-table operations API, or undefined if the page isn't ready. */
export function getTableApi(): TableApi | undefined {
    const exports = getEditablePageBundleExports();
    return exports ? exports.getTableApi() : undefined;
}
