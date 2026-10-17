// Plain-text rows for the Show Page History dialog, made in the page frame from what the page
// history reports (PageHistoryDetail) and from the user events that open transactions
// (pageHistoryInput.ts). The rows go to the recorder in the root window (pageHistoryRecorder.ts)
// as strings, so the record keeps no node of the page.

import type { PageHistoryEventKind } from "./pageHistoryRecorder";
import type {
    PageHistoryChangeContext,
    PageHistoryDetail,
} from "./pageHistoryTypes";

export interface PageHistoryDetailRow {
    kind: PageHistoryEventKind;
    text: string;
}

/** How many records of one batch get a row each; the rest get one row that counts them. */
export const kMaxRecordsShownPerBatch = 100;
// Texts and attribute values are cut to this many characters.
const kMaxValueLength = 80;
// A node's path keeps this many steps nearest the node.
const kMaxPathSteps = 8;
// A list of added or removed nodes names this many.
const kMaxNodesListed = 5;

/** The rows that one detail makes, oldest first. */
export function describePageHistoryDetail(
    detail: PageHistoryDetail,
): PageHistoryDetailRow[] {
    switch (detail.kind) {
        case "opened":
            return [
                transactionRow(
                    `opened ${detail.descriptionL10nKey} (depth ${detail.depth})`,
                ),
            ];
        case "ended":
            return [
                transactionRow(
                    `ended ${detail.descriptionL10nKey} (depth ${detail.depth})`,
                ),
            ];
        case "endWaits":
            return [
                transactionRow(
                    `end of ${detail.descriptionL10nKey} (depth ${detail.depth}) waits for ${detail.innerOpen} transaction(s) begun inside it`,
                ),
            ];
        case "closedWithNoStep":
            return [
                transactionRow(
                    `closed ${detail.descriptionL10nKey} with no net change, so no step`,
                ),
            ];
        case "cancelled":
            return [
                transactionRow(
                    `cancelled ${detail.descriptionL10nKey} (depth ${detail.depth}), reversing ${detail.recordsReversed} records`,
                ),
            ];
        case "changes":
            return describeChanges(
                detail.records,
                detail.ignored,
                detail.context,
            );
    }
}

/** A row about a transaction. */
function transactionRow(text: string): PageHistoryDetailRow {
    return { kind: "transaction", text };
}

/** One row for each record of a batch, up to kMaxRecordsShownPerBatch. */
function describeChanges(
    records: MutationRecord[],
    ignored: boolean[],
    context: PageHistoryChangeContext,
): PageHistoryDetailRow[] {
    const kind: PageHistoryEventKind =
        context.kind === "rollback" ? "rollback" : "change";
    const where = describeContext(context);
    const rows = records
        .slice(0, kMaxRecordsShownPerBatch)
        .map((record, i) => ({
            kind,
            text: `${where}: ${ignored[i] ? "[ignored by the filter] " : ""}${describeRecord(record)}`,
        }));
    if (records.length > kMaxRecordsShownPerBatch)
        rows.push({
            kind,
            text: `${where}: and ${records.length - kMaxRecordsShownPerBatch} more records in this batch`,
        });
    return rows;
}

/** Where a batch of changes went, as the start of each of its rows. */
export function describeContext(context: PageHistoryChangeContext): string {
    switch (context.kind) {
        case "transaction":
            return `in ${context.descriptionL10nKey} (depth ${context.depth})`;
        case "addToLatestTransaction":
        case "unmarked": {
            const how =
                context.kind === "unmarked"
                    ? "UNMARKED change"
                    : "addToLatestTransaction";
            if (context.descriptionL10nKey === undefined)
                return `${how}, with no step to join (becomes starting state)`;
            const joins =
                context.joins === "openTransaction"
                    ? "open transaction"
                    : "latest step";
            return `${how}, joins ${joins} ${context.descriptionL10nKey}`;
        }
        case "outsideHistory":
            return "outside history";
        case "beforeReady":
            return "before page ready";
        case "undo":
        case "redo":
        case "rollback":
            return `${context.kind} of ${context.descriptionL10nKey} put back`;
    }
}

/**
 * One record: where it happened, and what changed. A text or an attribute shows its old value
 * from the record and its value now, which is its value at the end of the batch.
 */
export function describeRecord(record: MutationRecord): string {
    const target = record.target;
    switch (record.type) {
        case "characterData":
            return `text of ${describePath(target)}: was ${quote(record.oldValue)}, now ${quote((target as CharacterData).data)}`;
        case "attributes": {
            const name = record.attributeName!;
            return `attribute ${name} of ${describePath(target)}: was ${quote(record.oldValue)}, now ${quote((target as Element).getAttribute(name))}`;
        }
        default: {
            const parts: string[] = [];
            if (record.addedNodes.length > 0)
                parts.push(`added ${describeNodes(record.addedNodes)}`);
            if (record.removedNodes.length > 0)
                parts.push(`removed ${describeNodes(record.removedNodes)}`);
            return `children of ${describePath(target)}: ${parts.join("; ")}`;
        }
    }
}

/**
 * A short path to node: from the nearest .bloom-editable (shown with its lang) or the
 * .bloom-page, or else from the top of the document, or "(detached)" when the node is out of any
 * document. Each step is the tag, id and first class, and the index among its parent's child
 * nodes.
 */
export function describePath(node: Node): string {
    const steps: string[] = [];
    let start = "(detached)";
    for (let n: Node | null = node; n; n = n.parentNode) {
        if (n.nodeType === Node.DOCUMENT_NODE) {
            start = "document";
            break;
        }
        if (n.nodeType === Node.ELEMENT_NODE) {
            const element = n as Element;
            if (element.classList.contains("bloom-editable")) {
                start = `editable[lang=${element.getAttribute("lang") ?? "?"}]`;
                break;
            }
            if (element.classList.contains("bloom-page")) {
                start = "page";
                break;
            }
        }
        steps.unshift(describeStep(n));
    }
    if (steps.length > kMaxPathSteps)
        steps.splice(0, steps.length - kMaxPathSteps, "...");
    return [start, ...steps].join(" > ");
}

/** One step of describePath. */
function describeStep(node: Node): string {
    const parent = node.parentNode;
    const index = parent
        ? `[${Array.prototype.indexOf.call(parent.childNodes, node)}]`
        : "";
    return describeNodeBriefly(node) + index;
}

/** A node's tag, id and first class, or "#text" and the like for a node that is not an element. */
function describeNodeBriefly(node: Node): string {
    if (node.nodeType !== Node.ELEMENT_NODE) return node.nodeName;
    const element = node as Element;
    const firstClass = element.classList[0];
    return (
        element.tagName.toLowerCase() +
        (element.id ? `#${element.id}` : "") +
        (firstClass ? `.${firstClass}` : "")
    );
}

/** Up to kMaxNodesListed nodes, each with the start of its text. */
function describeNodes(nodes: NodeList): string {
    const listed = Array.from(nodes)
        .slice(0, kMaxNodesListed)
        .map(
            (node) =>
                `${describeNodeBriefly(node)} ${quote(node.textContent ?? "")}`,
        );
    if (nodes.length > kMaxNodesListed)
        listed.push(`and ${nodes.length - kMaxNodesListed} more`);
    return listed.join(", ");
}

/**
 * A value in quotes, cut to kMaxValueLength characters, with characters that would not show
 * (controls, no-break and zero-width spaces, such as CKEditor's filling character) written as
 * \uXXXX; "(none)" for no value.
 */
export function quote(value: string | null | undefined): string {
    if (value === null || value === undefined) return "(none)";
    const cut =
        value.length > kMaxValueLength
            ? value.substring(0, kMaxValueLength) + "..."
            : value;
    const shown = cut.replace(
        // eslint-disable-next-line no-control-regex -- control characters are what this shows
        /[\u0000-\u001f\u007f\u00a0\u200b-\u200f\u2028\u2029\ufeff]/g,
        (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, "0")}`,
    );
    return `"${shown}"`;
}

/**
 * A row saying that a user event opened a transaction (InputTransactionOptions.onOpened): the
 * event, the key for a key press, the window it came from, and the element it went to.
 */
export function describeInputOpening(
    event: Event,
    kind: "pointer" | "event" | "typing",
): PageHistoryDetailRow {
    const key =
        "key" in event && typeof event.key === "string"
            ? ` ${quote(event.key)}`
            : "";
    const target = event.target as Node | null;
    const on =
        target && typeof target.nodeType === "number"
            ? ` on ${describePath(target)}`
            : "";
    return {
        kind: "input",
        text: `${event.type}${key} in ${describeEventWindow(event)}${on} opened a ${kind} transaction`,
    };
}

/** Which of the Edit tab's windows an event came from. */
function describeEventWindow(event: Event): string {
    const target = event.target as Node | Window | null;
    let eventWindow: Window | null | undefined;
    if (target && "nodeType" in target)
        eventWindow = target.ownerDocument?.defaultView;
    else eventWindow = (event as UIEvent).view ?? (target as Window | null);
    if (!eventWindow) return "an unknown window";
    const frameId = eventWindow.frameElement?.id;
    if (!eventWindow.frameElement) return "the root window";
    if (frameId === "page") return "the page";
    if (frameId === "toolbox") return "the toolbox";
    return `frame ${frameId || "(no id)"}`;
}
