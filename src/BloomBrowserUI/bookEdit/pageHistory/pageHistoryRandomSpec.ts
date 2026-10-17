// Randomized tests of the page history. Each sequence starts from a small page of Bloom-like
// markup and makes random undo steps of the kinds that Bloom's code makes: transactions (begun,
// sync, async and nested, some cancelled), changes outside any transaction that join the latest
// step, addToLatestTransaction, changes outside the history to what the edit-time filter ignores,
// and undos and redos in the middle followed by new changes. Within a step, the operations are
// those that real code does: text inserted and typed into, elements added, removed, replaced and
// moved, runs of nodes wrapped in a span and unwrapped again (as Talking Book does), attributes
// set and removed (including ones that the filter ignores), and nodes added and taken out again
// within one step. Between operations the observer may deliver its records, so that one step is
// made of several batches.
//
// After every step the test records the page as the history compares it (the kept part of every
// attribute and text, without the children that the filter ignores) and the node objects in it.
// Then it undoes back to the start and redoes to the end, checking each time that the page is the
// one recorded, with the same node objects, and that no undo or redo was refused.
//
// A failure names its seed and lists the actions of the sequence. To replay one, run this file
// with `-t "seed <n>"` (adding the seed to kSeeds if it is not there).
import { afterEach, describe, expect, it } from "vitest";
import { createPageHistory } from "./pageHistory";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
    type PageHistoryFilter,
} from "./pageHistoryTypes";
import { createEditTimeMarkupFilter } from "./editTimeMarkup";

// Enough sequences to run in a few seconds. A longer list searches further.
const kSeeds = Array.from({ length: 200 }, (_, i) => i + 1);

const kStartPage =
    '<div class="bloom-translationGroup" id="group">' +
    '<div class="bloom-editable" id="box1" lang="en" tabindex="0">' +
    '<p id="p1">Hello <b id="b1">bold</b> world</p>' +
    '<p id="p2"><span class="audio-sentence" id="s1">One.</span> <span class="audio-sentence" id="s2">Two.</span></p>' +
    "</div>" +
    '<div class="bloom-editable" id="box2" lang="fr"><p id="p3">Bonjour</p></div>' +
    "</div>" +
    '<div class="bloom-canvas" id="canvas">' +
    '<div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 20px;" data-bubble="{`style`:`none`}">' +
    '<div class="bloom-translationGroup" id="group2"><div class="bloom-editable" id="box3" lang="en"><p id="p4">Caption</p></div></div>' +
    "</div>" +
    '<div class="bloom-translationGroup bloom-imageDescription" id="desc">' +
    '<div class="bloom-editable" id="box4" lang="en"><p id="p5">An image</p></div>' +
    "</div>" +
    '<img id="img1" src="a.png" alt="">' +
    '<div class="bloom-ui" id="ui1">handle</div>' +
    "</div>";

const kTexts = ["a", "bb", " ", "Hello", "x y", "", " ", "Two."];
const kNewElementTags = ["span", "b", "i", "em", "p", "br"];
const kKeptClasses = [
    "audio-sentence",
    "bloom-visibility-code-on",
    "ui-audioCurrent",
];
const kEditTimeClasses = [
    "cke_focus",
    "overflow",
    "bloom-focusedCanvasElement",
    "bloom-selected",
];
// Attributes that the filter ignores wherever they are, with values to give them.
const kIgnoredAttributes: [string, string[]][] = [
    ["data-cke-saved-x", ["1", "2"]],
    ["data-bloom-active", ["true"]],
    ["data-hasqtip", ["1", "7"]],
    ["aria-describedby", ["qtip-1", "qtip-2"]],
];
// Attributes that the filter keeps, with values to give them.
const kKeptAttributes: [string, string[]][] = [
    ["data-x", ["1", "2", "three"]],
    ["data-duration", ["0.5", "1.25"]],
    ["lang", ["en", "fr", "tpi"]],
    ["aria-describedby", ["label-1"]],
];

/** mulberry32: a small seeded pseudo-random generator, so that a seed replays a sequence. */
function mulberry32(seed: number): () => number {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/** Choices made from a seeded generator. */
class Random {
    private readonly next: () => number;

    public constructor(seed: number) {
        this.next = mulberry32(seed);
    }

    /** A whole number from 0 up to, but not including, n. */
    public int(n: number): number {
        return Math.floor(this.next() * n);
    }

    /** True with probability p. */
    public chance(p: number): boolean {
        return this.next() < p;
    }

    /** One item of a list that must not be empty. */
    public pick<T>(items: readonly T[]): T {
        if (items.length === 0)
            throw new Error("test bug: picked from an empty list");
        return items[this.int(items.length)];
    }

    /** One key of a list of weighted choices. */
    public weighted<T>(choices: readonly [T, number][]): T {
        const total = choices.reduce((sum, choice) => sum + choice[1], 0);
        let r = this.next() * total;
        for (const [choice, weight] of choices) {
            r -= weight;
            if (r < 0) return choice;
        }
        return choices[choices.length - 1][0];
    }
}

/** The page as the history compares it, and the node objects in that part of it. */
interface Snapshot {
    markup: string;
    nodes: Node[];
}

/**
 * The page under root as the history compares it, following serializeKept in pageHistory.ts: the
 * children that the filter does not ignore, the kept part of each attribute in name order, and the
 * kept part of each text. A child that the filter leaves out of child lists alone, such as a
 * description box of a bloom-canvas, is in it after its parent's other children, since the history
 * records what is inside it but not its place among them.
 */
function takeSnapshot(root: Element, filter: PageHistoryFilter): Snapshot {
    const nodes: Node[] = [];
    const isIgnoredChild = (child: Node): boolean =>
        filter.ignoreNode(child) ||
        (child.nodeType === Node.TEXT_NODE &&
            filter.isIgnoredText(
                child as CharacterData,
                (child as CharacterData).data,
                "now",
            ));
    const filteredChildren = (parent: Node): Node[] => {
        const kept = Array.from(parent.childNodes).filter(
            (child) =>
                !isIgnoredChild(child) &&
                !filter.isLeftOutOfChildList(child, parent),
        );
        if (kept.length === 1 && filter.isLeftOutWhenOnlyChild(kept[0], parent))
            return [];
        return kept;
    };
    const serialize = (node: Node): string => {
        nodes.push(node);
        if (node.nodeType === Node.TEXT_NODE) {
            const text = node as CharacterData;
            return JSON.stringify(filter.keptText(text, text.data, "now"));
        }
        if (node.nodeType !== Node.ELEMENT_NODE) return `<!${node.nodeName}>`;
        const element = node as Element;
        const attributes = element
            .getAttributeNames()
            .sort()
            .map((name) => {
                const kept = filter.keptAttributeValue(
                    element,
                    name,
                    element.getAttribute(name) ?? undefined,
                );
                return kept === undefined
                    ? ""
                    : ` ${name}=${JSON.stringify(kept)}`;
            })
            .join("");
        const children = filteredChildren(element).map(serialize).join("");
        // The history records what is inside such a child, but not where it is among the others.
        const leftOut = Array.from(element.childNodes)
            .filter(
                (child) =>
                    !isIgnoredChild(child) &&
                    filter.isLeftOutOfChildList(child, element),
            )
            .map(serialize)
            .join("");
        const tag = element.tagName.toLowerCase();
        return `<${tag}${attributes}>${children}${leftOut ? `{left out of the child list: ${leftOut}}` : ""}</${tag}>`;
    };
    return { markup: serialize(root), nodes };
}

/** A short description of a node for the list of operations. */
function nodeLabel(node: Node): string {
    if (node.nodeType === Node.TEXT_NODE) {
        const where = node.parentElement
            ? ` in ${nodeLabel(node.parentElement)}`
            : " (detached)";
        return `text ${JSON.stringify((node as CharacterData).data)}${where}`;
    }
    const element = node as Element;
    const tag = element.tagName.toLowerCase();
    if (element.id) return `<${tag}#${element.id}>`;
    return `<${tag}>`;
}

/**
 * Where the snapshot after differs from expected, in the markup or in the node objects, or
 * undefined when it does not.
 */
function differenceBetween(
    actual: Snapshot,
    expected: Snapshot,
): string | undefined {
    if (actual.markup !== expected.markup)
        return `the page differs.\n  expected: ${expected.markup}\n  actual:   ${actual.markup}`;
    for (let i = 0; i < expected.nodes.length; i++) {
        if (actual.nodes[i] !== expected.nodes[i])
            return `node ${i}, ${nodeLabel(expected.nodes[i])}, is not the object that was there (it is ${nodeLabel(actual.nodes[i])}).`;
    }
    return undefined;
}

/** One part of an operation, which returns a line for the list of operations. */
interface OpPart {
    run: () => string;
    // True when the observer must deliver the records of the part before it, for a part that
    // changes a node that the part before took out of the page: in jsdom, the observer sees a
    // change to a removed node only once the history observes that node itself (pageHistory.ts,
    // observeRemovedNodes).
    needsDeliveryBefore?: boolean;
}

/** What kind of context the operations of one group run in. */
interface OpContext {
    // True inside an async context, where the observer may deliver records between parts.
    canDeliver: boolean;
    // True while a transaction is open, so that a node taken out of the page may be changed and
    // put back: outside a transaction, a change to a detached node is unseen by design, and the
    // check of rule 4 refuses an undo that would put it back (pageHistorySpec.ts, "refuses an
    // undo that would re-insert a subtree changed while it was detached").
    inTransaction: boolean;
    // True for changes outside the history, which may change only what the filter ignores.
    filteredOnly: boolean;
}

/** One random sequence of undo steps, undos and redos on its own page and history. */
class Sequence {
    private readonly rng: Random;
    public readonly page: HTMLElement;
    public readonly history: PageHistory;
    private readonly viewFilter = createEditTimeMarkupFilter();
    private readonly lines: string[] = [];
    private readonly refusals: string[] = [];
    private nextId = 1;
    private nextStep = 1;
    // The model: the page at the start, and after each step that undo would undo, oldest first,
    // with each step's description; and the steps that redo would redo, the next one last.
    private start: Snapshot;
    private undoModel: { description: string; after: Snapshot }[] = [];
    private redoModel: { description: string; after: Snapshot }[] = [];

    public constructor(public readonly seed: number) {
        this.rng = new Random(seed);
        this.page = document.createElement("div");
        this.page.className = "bloom-page";
        this.page.id = "page";
        this.page.innerHTML = kStartPage;
        document.body.appendChild(this.page);
        this.history = createPageHistory({
            root: this.page,
            filter: createEditTimeMarkupFilter(),
            maxUndoSteps: 100000,
            onUnmarkedChange: () => {},
            log: (line, kind) => {
                if (kind === "refused") this.refusals.push(line);
            },
        });
        this.start = this.snapshot();
    }

    /** Take the page and history away. */
    public dispose(): void {
        this.history.dispose();
        this.page.remove();
    }

    /** The page now, as the history compares it. */
    private snapshot(): Snapshot {
        return takeSnapshot(this.page, this.viewFilter);
    }

    /** The page that the model says the page should be now. */
    private expectedNow(): Snapshot {
        return this.undoModel[this.undoModel.length - 1]?.after ?? this.start;
    }

    /** Add a line to the list of what the sequence did. */
    private note(line: string): void {
        this.lines.push(line);
    }

    /** Fail with message, the seed and the list of what the sequence did. */
    private fail(message: string): never {
        throw new Error(
            `Randomized page history, seed ${this.seed}: ${message}\nWhat the sequence did:\n${this.lines.join("\n")}`,
        );
    }

    /** Fail unless the page is expected, with node objects and all. */
    private checkPage(expected: Snapshot, when: string): void {
        const difference = differenceBetween(this.snapshot(), expected);
        if (difference) this.fail(`${when}, ${difference}`);
    }

    /** Fail unless the history's undo and redo steps are those of the model, and none was refused. */
    private checkSteps(when: string): void {
        if (this.refusals.length > 0)
            this.fail(`${when}, the history refused: ${this.refusals[0]}`);
        const descriptions = this.history.getStepDescriptions();
        const undo = descriptions.undo.map((d) => d.l10nId).join(", ");
        const redo = descriptions.redo.map((d) => d.l10nId).join(", ");
        const expectedUndo = this.undoModel
            .map((s) => s.description)
            .join(", ");
        const expectedRedo = this.redoModel
            .map((s) => s.description)
            .reverse()
            .join(", ");
        if (undo !== expectedUndo || redo !== expectedRedo)
            this.fail(
                `${when}, the history has undo steps [${undo}] and redo steps [${redo}], not [${expectedUndo}] and [${expectedRedo}]`,
            );
    }

    /** Make the random sequence, then undo all of it and redo all of it, checking each step. */
    public async run(): Promise<void> {
        const actions = 6 + this.rng.int(12);
        for (let i = 1; i <= actions; i++) await this.randomAction(i);
        this.note("Undo everything, redo everything, undo everything:");
        while (this.undoModel.length > 0) await this.undo();
        while (this.redoModel.length > 0) await this.redo();
        while (this.undoModel.length > 0) await this.undo();
    }

    /** Do one random action: a step of some kind, or an undo or redo. */
    private async randomAction(index: number): Promise<void> {
        const kind = this.rng.weighted<string>([
            ["begun", 3],
            ["sync", 3],
            ["async", 2],
            ["nested", 3],
            ["unmarked", 2],
            ["addToLatest", 1],
            ["outsideHistory", 1],
            ["undo", this.undoModel.length > 0 ? 2 : 0],
            ["redo", this.redoModel.length > 0 ? 2 : 0],
        ]);
        switch (kind) {
            case "begun":
                return this.begunTransaction(index);
            case "sync":
                return this.syncTransaction(index);
            case "async":
                return this.asyncTransaction(index);
            case "nested":
                return this.nestedTransactions(index);
            case "unmarked":
                return this.unmarkedChanges(index);
            case "addToLatest":
                return this.addToLatestOutsideTransactions(index);
            case "outsideHistory":
                return this.changesOutsideHistory(index);
            case "undo":
                this.note(`${index}. undo`);
                return this.undo();
            case "redo":
                this.note(`${index}. redo`);
                return this.redo();
        }
    }

    /** A fresh step description. */
    private newDescription(): string {
        return `S${this.nextStep++}`;
    }

    /** A transaction from beginTransaction, ended or cancelled, with deliveries between parts. */
    private async begunTransaction(index: number): Promise<void> {
        const description = this.newDescription();
        this.note(`${index}. beginTransaction "${description}"`);
        const before = this.snapshot();
        const t = this.history.beginTransaction(description);
        await this.runOps(1 + this.rng.int(5), {
            canDeliver: true,
            inTransaction: true,
            filteredOnly: false,
        });
        if (this.rng.chance(0.15)) {
            this.note(`   cancel "${description}"`);
            t.cancel();
            this.checkPage(before, `after cancelling "${description}"`);
            this.checkSteps(`after cancelling "${description}"`);
            return;
        }
        this.note(`   end "${description}"`);
        t.end();
        this.noteTransactionClosed(description);
    }

    /** A doTransaction whose fn returns a plain value: all its changes are one batch. */
    private async syncTransaction(index: number): Promise<void> {
        const description = this.newDescription();
        this.note(`${index}. doTransaction "${description}" (sync)`);
        await this.history.doTransaction(description, () =>
            this.runOpsSync(1 + this.rng.int(5), {
                canDeliver: false,
                inTransaction: true,
                filteredOnly: false,
            }),
        );
        this.noteTransactionClosed(description);
    }

    /** A doTransaction whose fn is async, with deliveries between parts. */
    private async asyncTransaction(index: number): Promise<void> {
        const description = this.newDescription();
        this.note(`${index}. doTransaction "${description}" (async)`);
        await this.history.doTransaction(description, async () => {
            await this.runOps(1 + this.rng.int(5), {
                canDeliver: true,
                inTransaction: true,
                filteredOnly: false,
            });
        });
        this.noteTransactionClosed(description);
    }

    /**
     * An outermost begun transaction, often the stand-in with the general description, with
     * transactions nested inside it, some cancelled, and addToLatestTransaction inside it. When
     * the stand-in has changed nothing yet, addToLatestTransaction joins the latest step instead.
     */
    private async nestedTransactions(index: number): Promise<void> {
        const outerDescription = this.rng.chance(0.6)
            ? kGenericUndoDescriptionL10nKey
            : this.newDescription();
        this.note(`${index}. beginTransaction "${outerDescription}" (outer)`);
        let outerBefore = this.snapshot();
        const outer = this.history.beginTransaction(outerDescription);
        const parts = 1 + this.rng.int(4);
        const inTransaction: OpContext = {
            canDeliver: true,
            inTransaction: true,
            filteredOnly: false,
        };
        for (let part = 0; part < parts; part++) {
            const what = this.rng.weighted<string>([
                ["ops", 3],
                ["begun", 2],
                ["sync", 1],
                ["addToLatest", 2],
            ]);
            if (what === "ops") {
                await this.runOps(1 + this.rng.int(3), inTransaction);
            } else if (what === "addToLatest") {
                // Rule: a stand-in that has changed nothing is not yet the latest transaction.
                const joinsLatestStep =
                    outerDescription === kGenericUndoDescriptionL10nKey &&
                    differenceBetween(this.snapshot(), outerBefore) ===
                        undefined;
                this.note(
                    `   addToLatestTransaction${joinsLatestStep ? " (joins the latest step)" : ""}`,
                );
                const before = this.snapshot();
                this.history.addToLatestTransaction(() =>
                    this.runOpsSync(1 + this.rng.int(3), {
                        canDeliver: false,
                        inTransaction: !joinsLatestStep,
                        filteredOnly: false,
                    }),
                );
                if (joinsLatestStep) {
                    this.noteJoinedLatestStep(before, "addToLatestTransaction");
                    // What joined is part of the latest step, not of the outer transaction.
                    outerBefore = this.snapshot();
                }
            } else {
                const description = this.newDescription();
                const before = this.snapshot();
                if (what === "sync") {
                    this.note(
                        `   doTransaction "${description}" (sync, inner)`,
                    );
                    await this.history.doTransaction(description, () =>
                        this.runOpsSync(1 + this.rng.int(3), {
                            canDeliver: false,
                            inTransaction: true,
                            filteredOnly: false,
                        }),
                    );
                } else {
                    this.note(`   beginTransaction "${description}" (inner)`);
                    const inner = this.history.beginTransaction(description);
                    await this.runOps(1 + this.rng.int(3), inTransaction);
                    if (this.rng.chance(0.3)) {
                        this.note(`   cancel "${description}"`);
                        inner.cancel();
                        this.checkPage(
                            before,
                            `after cancelling the inner "${description}"`,
                        );
                    } else {
                        this.note(`   end "${description}"`);
                        inner.end();
                    }
                }
            }
        }
        if (this.rng.chance(0.15)) {
            this.note(`   cancel "${outerDescription}" (outer)`);
            outer.cancel();
            this.checkPage(
                outerBefore,
                "after cancelling the outer transaction",
            );
            this.checkSteps("after cancelling the outer transaction");
            return;
        }
        this.note(`   end "${outerDescription}" (outer)`);
        outer.end();
        this.noteTransactionClosed(outerDescription);
    }

    /** Changes outside any transaction, which join the latest step (rule 1). */
    private async unmarkedChanges(index: number): Promise<void> {
        this.note(`${index}. changes outside any transaction`);
        // Each batch that the observer delivers joins the latest step on its own.
        let before = this.snapshot();
        const noteBatch = () => {
            this.noteJoinedLatestStep(
                before,
                "changes outside any transaction",
            );
            before = this.snapshot();
        };
        await this.runOps(
            1 + this.rng.int(3),
            { canDeliver: true, inTransaction: false, filteredOnly: false },
            noteBatch,
        );
        await letObserverDeliver();
        noteBatch();
    }

    /** addToLatestTransaction with no transaction open: its changes join the latest step. */
    private async addToLatestOutsideTransactions(index: number): Promise<void> {
        this.note(`${index}. addToLatestTransaction`);
        const before = this.snapshot();
        this.history.addToLatestTransaction(() =>
            this.runOpsSync(1 + this.rng.int(3), {
                canDeliver: false,
                inTransaction: false,
                filteredOnly: false,
            }),
        );
        this.noteJoinedLatestStep(before, "addToLatestTransaction");
    }

    /** Changes inside doOutsideHistory, to what the filter ignores: no step may notice them. */
    private async changesOutsideHistory(index: number): Promise<void> {
        this.note(`${index}. doOutsideHistory`);
        const before = this.snapshot();
        this.history.doOutsideHistory(() =>
            this.runOpsSync(1 + this.rng.int(3), {
                canDeliver: false,
                inTransaction: false,
                filteredOnly: true,
            }),
        );
        this.checkPage(before, "after changing only what the filter ignores");
        this.checkSteps("after changing only what the filter ignores");
    }

    /**
     * Bring the model up to date after an outermost transaction closed: a new step if the history
     * made one, else no change to the page.
     */
    private noteTransactionClosed(description: string): void {
        const steps = this.history.getStepDescriptions().undo;
        if (steps.length === this.undoModel.length + 1) {
            this.undoModel.push({
                description: steps[steps.length - 1].l10nId,
                after: this.snapshot(),
            });
            this.redoModel = [];
        } else {
            this.checkPage(
                this.expectedNow(),
                `after "${description}" closed with no step`,
            );
        }
        this.checkSteps(`after "${description}" closed`);
    }

    /**
     * Bring the model up to date after changes that joined the latest step. A change to what the
     * history records clears redo. The latest step is gone if what joined put back everything
     * that it did; otherwise the page after it is the page now. With no latest step, the changes
     * become the page at the start.
     */
    private noteJoinedLatestStep(before: Snapshot, what: string): void {
        const after = this.snapshot();
        if (differenceBetween(after, before) === undefined) {
            this.checkSteps(`after ${what} that changed nothing that is kept`);
            return;
        }
        this.redoModel = [];
        const stepCount = this.history.getStepDescriptions().undo.length;
        if (this.undoModel.length === 0) this.start = after;
        else if (stepCount === this.undoModel.length)
            this.undoModel[this.undoModel.length - 1].after = after;
        else if (stepCount === this.undoModel.length - 1) {
            this.undoModel.pop();
            this.note("   (that put back everything the latest step did)");
            this.checkPage(
                this.expectedNow(),
                `after ${what} left the latest step with nothing to undo`,
            );
        } else
            this.fail(
                `after ${what}, the history has ${stepCount} undo steps, where the model has ${this.undoModel.length}`,
            );
        this.checkSteps(`after ${what}`);
    }

    /** Undo, and check that the page is as it was before the step. */
    private async undo(): Promise<void> {
        const step = this.undoModel.pop()!;
        await this.history.undo();
        this.redoModel.push(step);
        this.checkSteps(`after undoing "${step.description}"`);
        this.checkPage(
            this.expectedNow(),
            `after undoing "${step.description}"`,
        );
    }

    /** Redo, and check that the page is as it was after the step. */
    private async redo(): Promise<void> {
        const step = this.redoModel.pop()!;
        await this.history.redo();
        this.undoModel.push(step);
        this.checkSteps(`after redoing "${step.description}"`);
        this.checkPage(step.after, `after redoing "${step.description}"`);
    }

    /**
     * Run count random operations, letting the observer deliver between some of their parts, and
     * calling afterDelivery after each delivery.
     */
    private async runOps(
        count: number,
        context: OpContext,
        afterDelivery?: () => void,
    ): Promise<void> {
        const deliver = async () => {
            await letObserverDeliver();
            this.note("      (the observer delivers)");
            afterDelivery?.();
        };
        for (let i = 0; i < count; i++) {
            const parts = this.randomOp(context);
            for (let p = 0; p < parts.length; p++) {
                if (
                    p > 0 &&
                    (parts[p].needsDeliveryBefore || this.rng.chance(0.4))
                )
                    await deliver();
                this.note(`      ${parts[p].run()}`);
            }
            if (i < count - 1 && this.rng.chance(0.4)) await deliver();
        }
    }

    /** Run count random operations in one go, with no delivery between them. */
    private runOpsSync(count: number, context: OpContext): void {
        for (let i = 0; i < count; i++)
            this.randomOp(context).forEach((part) =>
                this.note(`      ${part.run()}`),
            );
    }

    /** The parts of one random operation that context allows and the page has a target for. */
    private randomOp(context: OpContext): OpPart[] {
        for (;;) {
            const parts = this.tryRandomOp(context);
            if (parts) return parts;
        }
    }

    /** The parts of one random operation, or undefined when the page has no target for it. */
    private tryRandomOp(context: OpContext): OpPart[] | undefined {
        if (context.filteredOnly) return this.filteredOnlyOp();
        const kind = this.rng.weighted<string>([
            ["insertText", 3],
            ["setText", 4],
            ["insertElement", 3],
            ["remove", 2],
            ["replace", 2],
            ["wrap", 2],
            ["unwrap", 2],
            ["move", 2],
            ["attribute", 3],
            ["class", 2],
            ["style", 1],
            ["filtered", 1],
            ["addAndTakeOut", 2],
            ["typeThenRewrap", 2],
            [
                "takeOutChangeAndPutBack",
                context.canDeliver && context.inTransaction ? 2 : 0,
            ],
        ]);
        switch (kind) {
            case "insertText":
                return this.insertTextOp();
            case "setText":
                return this.setTextOp();
            case "insertElement":
                return this.insertElementOp();
            case "remove":
                return this.removeOp();
            case "replace":
                return this.replaceOp();
            case "wrap":
                return this.wrapOp();
            case "unwrap":
                return this.unwrapOp();
            case "move":
                return this.moveOp();
            case "attribute":
                return this.attributeOp();
            case "class":
                return this.classOp(
                    kKeptClasses.concat(kEditTimeClasses),
                    true,
                );
            case "style":
                return this.styleOp();
            case "filtered":
                return this.filteredOnlyOp();
            case "addAndTakeOut":
                return this.addAndTakeOutOp();
            case "typeThenRewrap":
                return this.typeThenRewrapOp();
            case "takeOutChangeAndPutBack":
                return this.takeOutChangeAndPutBackOp();
        }
        return undefined;
    }

    /** The nodes in the page below the root, other than those in what the filter ignores. */
    private contentNodes(): Node[] {
        const result: Node[] = [];
        const walker = document.createTreeWalker(
            this.page,
            NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT,
            {
                acceptNode: (node) =>
                    this.viewFilter.ignoreNode(node)
                        ? NodeFilter.FILTER_REJECT
                        : NodeFilter.FILTER_ACCEPT,
            },
        );
        for (let node = walker.nextNode(); node; node = walker.nextNode())
            result.push(node);
        return result;
    }

    /** The elements of the page, root included, that may hold new children. */
    private containers(): Element[] {
        return [this.page as Element].concat(
            this.contentNodes().filter(
                (node): node is Element =>
                    node.nodeType === Node.ELEMENT_NODE &&
                    !(node as Element).matches("img, br"),
            ),
        );
    }

    /** The elements of the page, root included. */
    private elements(): Element[] {
        return this.containers().concat(
            this.contentNodes().filter(
                (node): node is Element =>
                    node.nodeType === Node.ELEMENT_NODE &&
                    (node as Element).matches("img, br"),
            ),
        );
    }

    /**
     * The elements that an operation may give a value that the filter keeps: all but br. Nothing
     * in Bloom sets such a value on a br, and the history can lose track of one: a step that left a
     * br as the only child of a block (CKEditor's filler, which the filter leaves out of the
     * block's lists), by taking out the text before it, has an empty list from after it, and its
     * redo takes the br out too if that text is back by then. Only a later step with a value of
     * the br's own notices, and the check of rule 4 refuses it. A later step that lists the br
     * among a block's children puts it back.
     */
    private elementsForKeptValues(): Element[] {
        return this.elements().filter((element) => element.tagName !== "BR");
    }

    /** The text nodes of the page. */
    private textNodes(): CharacterData[] {
        return this.contentNodes().filter(
            (node): node is CharacterData => node.nodeType === Node.TEXT_NODE,
        );
    }

    /**
     * Nodes that an operation may take out or move. Two kinds stay where they are, because the
     * filter leaves them out of child lists on the ground that only one piece of code moves them:
     * a description box of a bloom-canvas, which nothing in Bloom takes out of the page
     * (editTimeMarkup.ts, isDescriptionBoxOfABloomCanvas), and a br that is the only child of a
     * block of a box, which is CKEditor's filler, put in and taken out by CKEditor alone
     * (isCkEditorFillerBr). A step that moved such a br into a new span would leave it there on
     * undo, since no list records where it was, and a redo of the earlier step that added the br
     * would then take it out of the span, which the check of rule 4 rightly refuses.
     */
    private movableNodes(): Node[] {
        return this.contentNodes().filter((node) => this.isMovable(node));
    }

    /** True for a node that movableNodes allows. */
    private isMovable(node: Node): boolean {
        if (isDescriptionBox(node)) return false;
        const parent = node.parentNode;
        if (!parent) return true;
        return !this.isLoneFiller(this.keptChildren(parent), parent);
    }

    /** The children of parent that the filter keeps in its child lists, less those in without. */
    private keptChildren(parent: Node, without: Node[] = []): Node[] {
        return Array.from(parent.childNodes).filter(
            (child) =>
                !without.includes(child) &&
                !this.viewFilter.ignoreNode(child) &&
                !this.viewFilter.isLeftOutOfChildList(child, parent),
        );
    }

    /** True when children, as parent's kept children, are only a CKEditor filler br. */
    private isLoneFiller(children: Node[], parent: Node): boolean {
        return (
            children.length === 1 &&
            this.viewFilter.isLeftOutWhenOnlyChild(children[0], parent)
        );
    }

    /**
     * True when moving node into parent would make it a block's only child that the filter takes
     * for CKEditor's filler, or unwrapping node would make one of its children that. Only
     * CKEditor puts a filler br into an empty block, and the filter leaves the br out of the
     * block's lists, so that the move would be only half recorded: redo would take the br out of
     * where it was and put it nowhere.
     */
    private wouldMakeLoneFiller(node: Node, parent: Node): boolean {
        if (node.parentNode === parent)
            return this.isLoneFiller(
                this.keptChildren(parent, [node]).concat(
                    this.keptChildren(node),
                ),
                parent,
            );
        return (
            node.nodeName === "BR" &&
            this.keptChildren(parent, [node]).length === 0 &&
            this.viewFilter.isLeftOutWhenOnlyChild(node, parent)
        );
    }

    /**
     * A random position in a random container that does not lie inside node, or undefined when
     * there is none. When inBox is given, the container is inside a box (.bloom-editable) if
     * inBox is true, and outside every box if it is false. The root is a container outside every
     * box, so with no inBox there is always one.
     */
    private randomPlaceOutside(
        node?: Node,
        inBox?: boolean,
    ): { parent: Element; before: Node | null } | undefined {
        const parents = this.containers().filter(
            (c) =>
                (!node ||
                    (!node.contains(c) &&
                        !this.wouldMakeLoneFiller(node, c))) &&
                (inBox === undefined || isInBox(c) === inBox),
        );
        if (parents.length === 0) return undefined;
        const parent = this.rng.pick(parents);
        const children = Array.from(parent.childNodes).filter(
            (child) => child !== node,
        );
        const index = this.rng.int(children.length + 1);
        return { parent, before: children[index] ?? null };
    }

    /** A new element with an id, sometimes with text in it. */
    private newElement(tag?: string): Element {
        const element = document.createElement(
            tag ?? this.rng.pick(kNewElementTags),
        );
        element.id = `n${this.nextId++}`;
        if (element.tagName === "SPAN" && this.rng.chance(0.5))
            element.className = "audio-sentence";
        if (element.tagName !== "BR" && this.rng.chance(0.7))
            element.appendChild(document.createTextNode(this.rng.pick(kTexts)));
        return element;
    }

    /** Insert a new text node somewhere. */
    private insertTextOp(): OpPart[] {
        const { parent, before } = this.randomPlaceOutside()!;
        const text = this.rng.pick(kTexts);
        return [
            {
                run: () => {
                    parent.insertBefore(document.createTextNode(text), before);
                    return `insert text ${JSON.stringify(text)} into ${nodeLabel(parent)} before ${before ? nodeLabel(before) : "the end"}`;
                },
            },
        ];
    }

    /** Type into a text node: change its data in a few keystrokes. */
    private setTextOp(): OpPart[] | undefined {
        const nodes = this.textNodes();
        if (nodes.length === 0) return undefined;
        const node = this.rng.pick(nodes);
        const keystrokes = 1 + this.rng.int(3);
        const parts: OpPart[] = [];
        for (let k = 0; k < keystrokes; k++) {
            parts.push({
                run: () => {
                    const was = nodeLabel(node);
                    if (this.rng.chance(0.2) && node.data.length > 0)
                        node.deleteData(node.data.length - 1, 1);
                    else node.appendData(this.rng.pick(["a", "b", " "]));
                    return `type into ${was}: now ${JSON.stringify(node.data)}`;
                },
            });
        }
        return parts;
    }

    /** Insert a new element somewhere. */
    private insertElementOp(): OpPart[] {
        const { parent, before } = this.randomPlaceOutside()!;
        const element = this.newElement();
        return [
            {
                run: () => {
                    parent.insertBefore(element, before);
                    return `insert ${nodeLabel(element)} ${JSON.stringify(element.textContent)} into ${nodeLabel(parent)} before ${before ? nodeLabel(before) : "the end"}`;
                },
            },
        ];
    }

    /** Take a node out of the page. */
    private removeOp(): OpPart[] | undefined {
        const nodes = this.movableNodes();
        if (nodes.length === 0) return undefined;
        const node = this.rng.pick(nodes);
        return [
            {
                run: () => {
                    const was = nodeLabel(node);
                    node.parentNode!.removeChild(node);
                    return `remove ${was}`;
                },
            },
        ];
    }

    /** Replace a node with a new span. */
    private replaceOp(): OpPart[] | undefined {
        const nodes = this.movableNodes();
        if (nodes.length === 0) return undefined;
        const node = this.rng.pick(nodes);
        const span = this.newElement("span");
        return [
            {
                run: () => {
                    const was = nodeLabel(node);
                    node.parentNode!.replaceChild(span, node);
                    return `replace ${was} with ${nodeLabel(span)} ${JSON.stringify(span.textContent)}`;
                },
            },
        ];
    }

    /**
     * Wrap a run of children in a new span, as Talking Book's sentence markup does: either the
     * span goes into the page first and the run moves into it, or the run moves into the span
     * while the span is not yet in the page and the span then goes where the run was.
     */
    private wrapOp(): OpPart[] | undefined {
        const parents = this.containers().filter(
            (c) => c.childNodes.length > 0,
        );
        if (parents.length === 0) return undefined;
        const parent = this.rng.pick(parents);
        const start = this.rng.int(parent.childNodes.length);
        const count =
            1 + this.rng.int(Math.min(3, parent.childNodes.length - start));
        const run = Array.from(parent.childNodes).slice(start, start + count);
        if (!run.every((node) => this.isMovable(node))) return undefined;
        const span = document.createElement("span");
        span.id = `n${this.nextId++}`;
        span.className = "audio-sentence";
        const spanFirst = this.rng.chance(0.5);
        return [
            {
                run: () => {
                    const what = run.map(nodeLabel).join(", ");
                    if (spanFirst) {
                        parent.insertBefore(span, run[0]);
                        span.append(...run);
                    } else {
                        const before = run[run.length - 1].nextSibling;
                        span.append(...run);
                        parent.insertBefore(span, before);
                    }
                    return `wrap ${what} in ${nodeLabel(span)} (${spanFirst ? "span into the page first" : "span filled before it goes in"})`;
                },
            },
        ];
    }

    /** Take an element out, leaving its children where it was. */
    private unwrapOp(): OpPart[] | undefined {
        const elements = this.movableNodes().filter(
            (node): node is Element =>
                node.nodeType === Node.ELEMENT_NODE &&
                !(node as Element).classList.contains("bloom-editable") &&
                Array.from(node.childNodes).every((child) =>
                    this.isMovable(child),
                ) &&
                !this.wouldMakeLoneFiller(node, node.parentNode!),
        );
        if (elements.length === 0) return undefined;
        const element = this.rng.pick(elements);
        const childrenFirst = this.rng.chance(0.5);
        return [
            {
                run: () => {
                    const was = nodeLabel(element);
                    if (childrenFirst) {
                        while (element.firstChild)
                            element.parentNode!.insertBefore(
                                element.firstChild,
                                element,
                            );
                        element.remove();
                    } else
                        element.replaceWith(...Array.from(element.childNodes));
                    return `unwrap ${was} (${childrenFirst ? "children moved out first" : "replaceWith"})`;
                },
            },
        ];
    }

    /** Move a node somewhere else in the page. */
    private moveOp(): OpPart[] | undefined {
        const nodes = this.movableNodes();
        if (nodes.length === 0) return undefined;
        const node = this.rng.pick(nodes);
        const place = this.randomPlaceOutside(node, boxStateToKeep(node));
        if (!place) return undefined;
        const { parent, before } = place;
        return [
            {
                run: () => {
                    const was = nodeLabel(node);
                    parent.insertBefore(node, before);
                    return `move ${was} into ${nodeLabel(parent)} before ${before ? nodeLabel(before) : "the end"}`;
                },
            },
        ];
    }

    /** Set or remove an attribute, one that the filter keeps or one that it ignores. */
    private attributeOp(): OpPart[] {
        const element = this.rng.pick(this.elementsForKeptValues());
        const [name, values] = this.rng.pick(
            kKeptAttributes.concat(kIgnoredAttributes, [
                ["tabindex", ["0", "1"]],
            ]),
        );
        const value = this.rng.chance(0.25) ? undefined : this.rng.pick(values);
        return [
            {
                run: () => {
                    if (value === undefined) element.removeAttribute(name);
                    else element.setAttribute(name, value);
                    return value === undefined
                        ? `remove ${name} from ${nodeLabel(element)}`
                        : `set ${name}="${value}" on ${nodeLabel(element)}`;
                },
            },
        ];
    }

    /** Add or remove one class token. */
    private classOp(tokens: string[], keptOnes: boolean): OpPart[] {
        const element = this.rng.pick(
            keptOnes ? this.elementsForKeptValues() : this.elements(),
        );
        const token = this.rng.pick(tokens);
        return [
            {
                run: () => {
                    const added = element.classList.toggle(token);
                    return `${added ? "add" : "remove"} class ${token} ${added ? "to" : "from"} ${nodeLabel(element)}`;
                },
            },
        ];
    }

    /** Set or remove an inline style property that the filter keeps. */
    private styleOp(): OpPart[] {
        const element = this.rng.pick(
            this.elementsForKeptValues(),
        ) as HTMLElement;
        const property = this.rng.pick(["left", "top"]);
        const value = this.rng.chance(0.25)
            ? undefined
            : `${this.rng.int(50)}px`;
        return [
            {
                run: () => {
                    if (value === undefined)
                        element.style.removeProperty(property);
                    else element.style.setProperty(property, value);
                    return value === undefined
                        ? `remove style ${property} from ${nodeLabel(element)}`
                        : `set style ${property}: ${value} on ${nodeLabel(element)}`;
                },
            },
        ];
    }

    /**
     * A change to what the edit-time filter ignores: an edit-time class, an attribute that the
     * filter ignores, a box's min-height, or a .bloom-ui element added or taken out.
     */
    private filteredOnlyOp(): OpPart[] | undefined {
        const kind = this.rng.weighted<string>([
            ["class", 3],
            ["attribute", 3],
            ["minHeight", 1],
            ["boxAttribute", 1],
            ["addUi", 1],
            ["removeUi", 1],
        ]);
        switch (kind) {
            case "class":
                return this.classOp(kEditTimeClasses, false);
            case "attribute": {
                const element = this.rng.pick(this.elements());
                const [name, values] = this.rng.pick(kIgnoredAttributes);
                const value = this.rng.chance(0.3)
                    ? undefined
                    : this.rng.pick(values);
                // aria-describedby is ignored only with a qtip value: replacing or removing
                // another value is a change that the filter keeps.
                const current = element.getAttribute(name) ?? undefined;
                if (
                    this.viewFilter.keptAttributeValue(
                        element,
                        name,
                        current,
                    ) !== undefined
                )
                    return undefined;
                return [
                    {
                        run: () => {
                            if (value === undefined)
                                element.removeAttribute(name);
                            else element.setAttribute(name, value);
                            return value === undefined
                                ? `remove ${name} from ${nodeLabel(element)}`
                                : `set ${name}="${value}" on ${nodeLabel(element)}`;
                        },
                    },
                ];
            }
            case "minHeight":
            case "boxAttribute": {
                const boxes = this.elements().filter((e) =>
                    e.classList.contains("bloom-editable"),
                );
                if (boxes.length === 0) return undefined;
                const box = this.rng.pick(boxes) as HTMLElement;
                const value = `${this.rng.int(9)}`;
                return [
                    {
                        run: () => {
                            if (kind === "minHeight") {
                                box.style.setProperty(
                                    "min-height",
                                    `${value}px`,
                                );
                                return `set style min-height: ${value}px on ${nodeLabel(box)}`;
                            }
                            box.setAttribute("tabindex", value);
                            return `set tabindex="${value}" on ${nodeLabel(box)}`;
                        },
                    },
                ];
            }
            case "addUi": {
                const { parent, before } = this.randomPlaceOutside()!;
                const ui = document.createElement("div");
                ui.className = "bloom-ui";
                ui.id = `n${this.nextId++}`;
                ui.textContent = "ui";
                return [
                    {
                        run: () => {
                            parent.insertBefore(ui, before);
                            return `insert ${nodeLabel(ui)} (.bloom-ui) into ${nodeLabel(parent)}`;
                        },
                    },
                ];
            }
            case "removeUi": {
                const uis = Array.from(this.page.querySelectorAll(".bloom-ui"));
                if (uis.length === 0) return undefined;
                const ui = this.rng.pick(uis);
                return [
                    {
                        run: () => {
                            ui.remove();
                            return `remove ${nodeLabel(ui)} (.bloom-ui)`;
                        },
                    },
                ];
            }
        }
        return undefined;
    }

    /** Add a new node, change it, and take it out again. */
    private addAndTakeOutOp(): OpPart[] {
        const { parent, before } = this.randomPlaceOutside()!;
        const element = this.newElement("span");
        return [
            {
                run: () => {
                    parent.insertBefore(element, before);
                    return `insert ${nodeLabel(element)} into ${nodeLabel(parent)}`;
                },
            },
            {
                run: () => {
                    element.setAttribute("data-x", "changed");
                    element.appendChild(document.createTextNode("more"));
                    return `change ${nodeLabel(element)}`;
                },
            },
            {
                run: () => {
                    element.remove();
                    return `remove ${nodeLabel(element)}`;
                },
            },
        ];
    }

    /**
     * What typing with Talking Book open does: make a text node, type into it, then replace it
     * with a new sentence span (the bug that commit f1663399c5 fixed).
     */
    private typeThenRewrapOp(): OpPart[] {
        const { parent, before } = this.randomPlaceOutside()!;
        const typed = document.createTextNode(this.rng.pick(["a", " d"]));
        const span = document.createElement("span");
        span.id = `n${this.nextId++}`;
        span.className = "audio-sentence";
        return [
            {
                run: () => {
                    parent.insertBefore(typed, before);
                    return `insert ${nodeLabel(typed)}`;
                },
            },
            {
                run: () => {
                    typed.appendData("d");
                    return `type into it: now ${JSON.stringify(typed.data)}`;
                },
            },
            {
                run: () => {
                    span.textContent = typed.data;
                    typed.parentNode!.replaceChild(span, typed);
                    return `replace that text with ${nodeLabel(span)} ${JSON.stringify(span.textContent)}`;
                },
            },
        ];
    }

    /**
     * Take a node out of the page, change it, and sometimes put it back somewhere, inside one
     * transaction.
     */
    private takeOutChangeAndPutBackOp(): OpPart[] | undefined {
        const nodes = this.movableNodes();
        if (nodes.length === 0) return undefined;
        const node = this.rng.pick(nodes);
        const inBox = boxStateToKeep(node);
        const putBack = this.rng.chance(0.7);
        const parts: OpPart[] = [
            {
                run: () => {
                    const was = nodeLabel(node);
                    node.parentNode!.removeChild(node);
                    return `take out ${was}`;
                },
            },
            {
                needsDeliveryBefore: true,
                run: () => {
                    if (node.nodeType === Node.TEXT_NODE) {
                        (node as CharacterData).appendData("!");
                        return `change the text of ${nodeLabel(node)} while it is out`;
                    }
                    (node as Element).setAttribute("data-x", "out");
                    return `set data-x="out" on ${nodeLabel(node)} while it is out`;
                },
            },
        ];
        if (putBack)
            parts.push({
                run: () => {
                    const place = this.randomPlaceOutside(node, inBox);
                    if (!place)
                        return `leave ${nodeLabel(node)} out: no place keeps it in or out of a box as it was`;
                    const { parent, before } = place;
                    parent.insertBefore(node, before);
                    return `put ${nodeLabel(node)} back into ${nodeLabel(parent)}`;
                },
            });
        return parts;
    }
}

/** True for a node inside a box (.bloom-editable), or that is one. */
function isInBox(node: Node): boolean {
    const element =
        node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement;
    return !!element?.closest(".bloom-editable");
}

/**
 * Whether a node that an operation moves must stay inside a box, or outside every box: undefined
 * when it may go anywhere. A br in a block inside a box may be CKEditor's filler, which the filter
 * leaves out of child lists when it is a block's only child (isCkEditorFillerBr), and whether it
 * is depends on the box around it. Nothing in Bloom moves a block out of its box or into one, or
 * takes a box away from round its blocks, so the answer never changes for a br in the page; a
 * move that changed it would change a child list that nothing records.
 */
function boxStateToKeep(node: Node): boolean | undefined {
    if (node.nodeType !== Node.ELEMENT_NODE || !node.parentNode)
        return undefined;
    const element = node as Element;
    const brs =
        element.tagName === "BR"
            ? [element]
            : Array.from(element.querySelectorAll("br"));
    // A br inside a box that moves with it is unaffected.
    const dependsOnPlace = brs.some((br) => {
        const box = br.closest(".bloom-editable");
        return !box || !element.contains(box);
    });
    return dependsOnPlace ? isInBox(node.parentNode) : undefined;
}

/** True for a description box of a bloom-canvas. */
function isDescriptionBox(node: Node): boolean {
    return (
        node.nodeType === Node.ELEMENT_NODE &&
        (node as Element).classList.contains("bloom-imageDescription")
    );
}

// MutationObserver queues its callback as a microtask when a change is made, before this await
// queues its own continuation, so by the time this resolves the observer has delivered every
// change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

let current: Sequence | undefined;

afterEach(() => {
    current?.dispose();
    current = undefined;
});

describe("page history: random sequences of steps undo and redo exactly", () => {
    it.each(kSeeds)("seed %i", async (seed) => {
        current = new Sequence(seed);
        expect(
            current.page.querySelector("#desc"),
            "test setup: the page should have its description box",
        ).not.toBeNull();
        await current.run();
    });
});

// The smallest cases of the failures that the random sequences found, kept as fixed tests.
describe("page history: cases found by the random sequences", () => {
    let page: HTMLElement | undefined;
    let history: PageHistory | undefined;
    let refusals: string[] = [];

    afterEach(() => {
        history?.dispose();
        page?.remove();
        history = undefined;
        page = undefined;
        refusals = [];
    });

    // A page with two boxes, and a history on it that notes every refused undo or redo.
    function setUp(): { page: HTMLElement; history: PageHistory } {
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML =
            '<div class="bloom-translationGroup">' +
            '<div class="bloom-editable" lang="en"><p id="p1">Hello <b id="b1">bold</b> world</p></div>' +
            '<div class="bloom-editable" lang="fr"><p id="p2">Bonjour</p></div>' +
            "</div>";
        document.body.appendChild(page);
        history = createPageHistory({
            root: page,
            filter: createEditTimeMarkupFilter(),
            onUnmarkedChange: () => {},
            log: (line, kind) => {
                if (kind === "refused") refusals.push(line);
            },
        });
        return { page, history };
    }

    function byId(id: string): HTMLElement {
        const element = page!.querySelector<HTMLElement>(`#${id}`);
        expect(
            element,
            `test setup: #${id} should be on the page`,
        ).not.toBeNull();
        return element!;
    }

    // One way Talking Book could wrap: the run moves into the span before the span goes in.
    it("redoes a wrap whose span was filled before it went into the page", async () => {
        const { page, history } = setUp();
        const p1 = byId("p1");
        const run = Array.from(p1.childNodes);
        expect(run.length, "test setup").toBe(3);
        const beforeHtml = page.outerHTML;
        const span = document.createElement("span");
        span.className = "audio-sentence";
        await history.doTransaction("Wrap", () => {
            span.append(...run);
            p1.appendChild(span);
        });
        const afterHtml = page.outerHTML;

        await history.undo();
        expect(page.outerHTML).toBe(beforeHtml);
        expect(p1.firstChild).toBe(run[0]);
        await history.redo();

        expect(refusals).toEqual([]);
        expect(page.outerHTML).toBe(afterHtml);
        expect(Array.from(span.childNodes)).toEqual(run);
    });

    it("undoes a step that recorded a new span's children only after a node of the page moved into it", async () => {
        const { page, history } = setUp();
        const p1 = byId("p1");
        const b1 = byId("b1");
        const beforeHtml = page.outerHTML;
        const span = document.createElement("span");
        const t = history.beginTransaction("Move into a new span");
        span.appendChild(b1);
        byId("p2").appendChild(span);
        await letObserverDeliver();
        // The span's first record: its children before that already hold b1.
        span.appendChild(document.createTextNode("!"));
        await letObserverDeliver();
        t.end();

        await history.undo();

        expect(refusals).toEqual([]);
        expect(b1.parentNode, "b1 should be back in p1").toBe(p1);
        expect(page.outerHTML).toBe(beforeHtml);
    });

    it("redoes a wrap, filled before it went in, of a node that the step itself added", async () => {
        const { page, history } = setUp();
        const outer = document.createElement("span");
        const text = document.createTextNode("new");
        outer.appendChild(text);
        const inner = document.createElement("span");
        const t = history.beginTransaction("Add and wrap");
        byId("p2").appendChild(outer);
        await letObserverDeliver();
        inner.appendChild(text);
        outer.appendChild(inner);
        await letObserverDeliver();
        t.end();
        const afterHtml = page.outerHTML;

        await history.undo();
        await history.redo();

        expect(refusals).toEqual([]);
        expect(page.outerHTML).toBe(afterHtml);
        expect(text.parentNode).toBe(inner);
    });

    it("undoes a step that added new spans, from which a change joining it took them out", async () => {
        const { page, history } = setUp();
        const beforeHtml = page.outerHTML;
        const a = document.createElement("span");
        const b = document.createElement("span");
        const text = document.createTextNode("x");
        b.appendChild(text);
        a.appendChild(b);
        await history.doTransaction("Add", () => {
            byId("p2").appendChild(a);
            byId("p1").appendChild(document.createTextNode("!"));
        });
        // Changes that join the step: wrap the text in c, which goes into b first, type into
        // the text, and take out a.
        const c = document.createElement("span");
        b.insertBefore(c, text);
        c.appendChild(text);
        await letObserverDeliver();
        text.data = "xy";
        await letObserverDeliver();
        a.remove();
        await letObserverDeliver();
        expect(history.undoDescriptionL10nKey, "test setup").toBe("Add");

        await history.undo();

        expect(refusals).toEqual([]);
        expect(page.outerHTML).toBe(beforeHtml);
    });

    it("lets addToLatestTransaction join the latest step when the stand-in only added a node and took it out", async () => {
        const { history } = setUp();
        await history.doTransaction("Type", () => {
            byId("p2").appendChild(document.createTextNode("a"));
        });
        const standIn = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        const span = document.createElement("span");
        byId("p1").appendChild(span);
        await letObserverDeliver();
        span.setAttribute("data-x", "changed");
        span.remove();
        history.addToLatestTransaction(() => {
            byId("p2").setAttribute("data-x", "marked up");
        });
        standIn.end();

        expect(
            history.getStepDescriptions().undo.map((d) => d.l10nId),
            "the markup should join the step Type, not make a step of its own",
        ).toEqual(["Type"]);
        await history.undo();
        expect(byId("p2").hasAttribute("data-x")).toBe(false);
        expect(byId("p2").textContent).toBe("Bonjour");
    });
});
