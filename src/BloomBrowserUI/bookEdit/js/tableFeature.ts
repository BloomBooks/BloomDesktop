// The `table` feature's status for the page currently being edited, held where
// synchronous code can ask about it.
//
// Three places have to answer "may this table be restructured?" during an event
// they cannot await: the bloom-table library's structural chrome gate (it asks as
// the chrome is about to be shown), a right-click on a cell (which decides between
// the library's Cell menu and Bloom's text menu), and a mouse-down on a row or
// column boundary (which decides whether to start a resize drag). The feature
// status comes from the server, so it is fetched once per page load and the answer
// kept here.
//
// The two halves of the status mean different things and both have to be true. A
// tier below Pro leaves `enabled` false, and turning the Tables experiment off
// leaves `visible` false. Either way the rule is the canvas rule: a table already
// in the book stays editable as text, but nothing may create or restructure one.
import { getFeatureStatusAsync } from "../../react_components/featureStatus";

// Starts out false so that a table attached before the status arrives is gated
// rather than briefly showing chrome the user is not entitled to. Nothing has to
// tell the library the answer has changed: it asks the gate afresh each time it is
// about to show or reposition the chrome, and the chrome only appears once the
// user selects a cell.
let tablesMayBeRestructuredOnThisPage = false;

/**
 * True when the user may create and restructure tables: add and remove rows and
 * columns, resize them, change a cell's content type, and duplicate a table. False
 * both below Pro and with the Tables experiment turned off.
 */
export function tablesMayBeRestructured(): boolean {
    return tablesMayBeRestructuredOnThisPage;
}

let pendingRefresh: Promise<void> | undefined;

/**
 * Fetch the `table` feature's status and remember it for the synchronous callers
 * above. Called from SetupTableEditing, which runs on every page load and again
 * whenever a canvas element is added; the answer cannot change within one page
 * load (this module lives in the page iframe), so the server is asked once and
 * the same promise is handed back after that.
 */
export function refreshTableFeatureStatus(): Promise<void> {
    if (!pendingRefresh) {
        pendingRefresh = getFeatureStatusAsync("table").then((status) => {
            tablesMayBeRestructuredOnThisPage =
                !!status?.enabled && !!status?.visible;
        });
    }
    return pendingRefresh;
}

/**
 * Set the remembered answer directly. For tests, which have no server to ask; page
 * code calls refreshTableFeatureStatus instead.
 */
export function setTablesMayBeRestructuredForTests(mayBe: boolean): void {
    tablesMayBeRestructuredOnThisPage = mayBe;
}
