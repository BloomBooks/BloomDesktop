// DOM helpers for the page history (pageHistory.ts): working out a child list from mutation
// records, reversing a record, putting child lists back, and saving and restoring the selection.
// They know nothing about transactions or steps.

/**
 * The children that parent had before the given childList records, which must be every childList
 * record for parent since that moment, oldest first, with nothing changed since the last of them.
 * Works back from parent's current children by reversing the records, newest first. The result
 * is the same node objects, ignored nodes included.
 */
export function childrenBeforeRecords(
    parent: Node,
    records: MutationRecord[],
): Node[] {
    const children: Node[] = Array.from(parent.childNodes);
    for (let i = records.length - 1; i >= 0; i--) {
        const record = records[i];
        record.addedNodes.forEach((added) => {
            const index = children.indexOf(added);
            if (index >= 0) children.splice(index, 1);
        });
        children.splice(
            insertionIndex(children, record),
            0,
            ...Array.from(record.removedNodes),
        );
    }
    return children;
}

/**
 * Where, in a child list that the record's added nodes have been taken out of, the record's
 * removed nodes go back: straight after previousSibling, or first when there was none.
 */
function insertionIndex(children: Node[], record: MutationRecord): number {
    if (!record.previousSibling) return 0;
    const previousIndex = children.indexOf(record.previousSibling);
    if (previousIndex >= 0) return previousIndex + 1;
    // The list does not match the record. Fall back on nextSibling, then on the end.
    const nextIndex = record.nextSibling
        ? children.indexOf(record.nextSibling)
        : -1;
    return nextIndex >= 0 ? nextIndex : children.length;
}

/**
 * Put back on the page the state from before one mutation record, from the old value that the
 * record carries. Reversing a run of records newest first takes the page back through the states
 * it passed through, so each record's siblings are in place when its turn comes.
 */
export function reverseRecord(record: MutationRecord): void {
    switch (record.type) {
        case "attributes": {
            const element = record.target as Element;
            const name = record.attributeName!;
            if (record.oldValue === null) element.removeAttribute(name);
            else element.setAttribute(name, record.oldValue);
            break;
        }
        case "characterData":
            (record.target as CharacterData).data = record.oldValue ?? "";
            break;
        case "childList": {
            const parent = record.target;
            record.addedNodes.forEach((added) => {
                if (added.parentNode === parent) parent.removeChild(added);
            });
            const reference = referenceChildForReinsertion(record);
            record.removedNodes.forEach((removed) =>
                parent.insertBefore(removed, reference),
            );
            break;
        }
    }
}

/**
 * The child of record.target before which reverseRecord puts the removed nodes back, or null for
 * the end. The record's added nodes must already be out.
 */
function referenceChildForReinsertion(record: MutationRecord): Node | null {
    const parent = record.target;
    if (!record.previousSibling) return parent.firstChild;
    if (record.previousSibling.parentNode === parent)
        return record.previousSibling.nextSibling;
    if (record.nextSibling && record.nextSibling.parentNode === parent)
        return record.nextSibling;
    return null;
}

/**
 * Give each parent in lists exactly the listed children, in that order, as the same node objects,
 * moving them from wherever they are. Children for which isIgnored is true are not in the lists
 * and stay where they are among the others. First every parent loses the children that it should
 * not have, then each gets the ones it lacks. Removing first means that a node is never inserted
 * into its own descendant, whatever order the parents come in, because what remains after the
 * removals is part of the tree that the lists describe.
 */
export function putBackChildLists(
    lists: Map<Node, Node[]>,
    isIgnored: (child: Node) => boolean,
): void {
    lists.forEach((wanted, parent) => {
        const wantedSet = new Set(wanted);
        Array.from(parent.childNodes).forEach((child) => {
            if (!isIgnored(child) && !wantedSet.has(child))
                parent.removeChild(child);
        });
    });
    lists.forEach((wanted, parent) => {
        let cursor = parent.firstChild;
        for (const child of wanted) {
            while (cursor && cursor !== child && isIgnored(cursor))
                cursor = cursor.nextSibling;
            if (cursor === child) {
                cursor = cursor.nextSibling;
                continue;
            }
            parent.insertBefore(child, cursor);
        }
    });
}

/** True when the two lists hold the same node objects in the same order. */
export function sameNodes(a: Node[], b: Node[]): boolean {
    return a.length === b.length && a.every((node, i) => node === b[i]);
}

/**
 * A node's subtree as text, to tell later whether a detached subtree has changed: the markup of
 * an element, the text of anything else.
 */
export function serializeNode(node: Node): string {
    if (node.nodeType === Node.ELEMENT_NODE) return (node as Element).outerHTML;
    return node.textContent ?? "";
}

/**
 * A short description of a node for a development warning: the element's tag with its id and
 * classes, or, for a text node, its parent's.
 */
export function describeNode(node: Node): string {
    if (node.nodeType !== Node.ELEMENT_NODE) {
        return node.parentElement
            ? `a text node in ${describeNode(node.parentElement)}`
            : "a detached text node";
    }
    const element = node as Element;
    let description = "<" + element.tagName.toLowerCase();
    if (element.id) description += ` id="${element.id}"`;
    const className = element.getAttribute("class");
    if (className) description += ` class="${className}"`;
    return description + ">";
}

/** The selection as it was at one moment. The nodes are kept, so the offsets stay meaningful. */
export interface SavedSelection {
    anchorNode: Node;
    anchorOffset: number;
    focusNode: Node;
    focusOffset: number;
}

/** The document's selection now, or undefined when it has none. */
export function saveSelection(doc: Document): SavedSelection | undefined {
    const selection = doc.getSelection();
    if (!selection || !selection.anchorNode || !selection.focusNode)
        return undefined;
    return {
        anchorNode: selection.anchorNode,
        anchorOffset: selection.anchorOffset,
        focusNode: selection.focusNode,
        focusOffset: selection.focusOffset,
    };
}

/**
 * Select what saved selected, when both its nodes are inside root and its offsets still fit them;
 * otherwise leave the selection alone.
 */
export function restoreSelection(
    doc: Document,
    root: Node,
    saved: SavedSelection | undefined,
): void {
    if (!saved) return;
    if (
        !fitsNode(root, saved.anchorNode, saved.anchorOffset) ||
        !fitsNode(root, saved.focusNode, saved.focusOffset)
    )
        return;
    doc.getSelection()?.setBaseAndExtent(
        saved.anchorNode,
        saved.anchorOffset,
        saved.focusNode,
        saved.focusOffset,
    );
}

/** True when node is inside root and offset is a position in it. */
function fitsNode(root: Node, node: Node, offset: number): boolean {
    if (!root.contains(node)) return false;
    const length =
        node.nodeType === Node.ELEMENT_NODE
            ? node.childNodes.length
            : (node.textContent ?? "").length;
    return offset <= length;
}
