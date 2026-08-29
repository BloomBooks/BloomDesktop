import { describe, it, expect, beforeEach, afterEach } from "vitest";
import {
    kMinHeightForInBoxAffordances,
    kMinWidthForInBoxAffordances,
    kTooSmallForInBoxAffordancesClass,
    observeTranslationGroupSizes,
    stopObservingTranslationGroupSizes,
    updateInBoxAffordanceMarking,
} from "./translationGroupSizeMarking";

// jsdom lays nothing out, so offsetWidth and offsetHeight are always zero. Each test says
// what size it wants a group to have.
function setSize(element: HTMLElement, width: number, height: number): void {
    Object.defineProperty(element, "offsetWidth", {
        value: width,
        configurable: true,
    });
    Object.defineProperty(element, "offsetHeight", {
        value: height,
        configurable: true,
    });
}

// jsdom has no ResizeObserver. This stand-in remembers what it was asked to watch and lets
// a test say "these changed size", which is what the real one does when a splitter is
// dragged or a table column is resized.
class FakeResizeObserver {
    public static instances: FakeResizeObserver[] = [];
    public observed: HTMLElement[] = [];
    public disconnected = false;
    public constructor(
        private callback: (entries: { target: HTMLElement }[]) => void,
    ) {
        FakeResizeObserver.instances.push(this);
    }
    public observe(element: HTMLElement): void {
        if (!this.observed.includes(element)) this.observed.push(element);
    }
    public unobserve(element: HTMLElement): void {
        this.observed = this.observed.filter((e) => e !== element);
    }
    public disconnect(): void {
        this.disconnected = true;
        this.observed = [];
    }
    public reportResize(elements: HTMLElement[]): void {
        this.callback(elements.map((target) => ({ target })));
    }
}

function makeGroup(width: number, height: number): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup";
    setSize(group, width, height);
    return group;
}

// Comfortably over both thresholds, so nothing about the numbers below is borderline.
const kBigWidth = kMinWidthForInBoxAffordances + 100;
const kBigHeight = kMinHeightForInBoxAffordances + 100;

describe("translationGroupSizeMarking", () => {
    beforeEach(() => {
        FakeResizeObserver.instances = [];
        (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
            FakeResizeObserver;
        document.body.innerHTML = "";
    });

    afterEach(() => stopObservingTranslationGroupSizes(document.body));

    describe("updateInBoxAffordanceMarking", () => {
        it("leaves a group that is big in both directions unmarked", () => {
            const group = makeGroup(kBigWidth, kBigHeight);
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });

        it("marks a group that is short", () => {
            const group = makeGroup(
                kBigWidth,
                kMinHeightForInBoxAffordances - 1,
            );
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);
        });

        it("marks a group that is narrow", () => {
            const group = makeGroup(
                kMinWidthForInBoxAffordances - 1,
                kBigHeight,
            );
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);
        });

        it("leaves a group exactly at both thresholds unmarked", () => {
            const group = makeGroup(
                kMinWidthForInBoxAffordances,
                kMinHeightForInBoxAffordances,
            );
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });

        it("takes the mark off again when the group grows", () => {
            const group = makeGroup(10, 10);
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);
            setSize(group, kBigWidth, kBigHeight);
            updateInBoxAffordanceMarking(group);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });
    });

    describe("observeTranslationGroupSizes", () => {
        it("marks the small groups within the container and not the big ones", () => {
            const small = makeGroup(20, 20);
            const big = makeGroup(kBigWidth, kBigHeight);
            document.body.append(small, big);

            observeTranslationGroupSizes(document.body);

            expect(
                small.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);
            expect(
                big.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });

        it("marks the container itself when it is a translation group", () => {
            const group = makeGroup(20, 20);
            document.body.appendChild(group);

            observeTranslationGroupSizes(group);

            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);
        });

        it("watches every group it marked", () => {
            const small = makeGroup(20, 20);
            const big = makeGroup(kBigWidth, kBigHeight);
            document.body.append(small, big);

            observeTranslationGroupSizes(document.body);

            const observer = FakeResizeObserver.instances[0];
            expect(observer.observed).toEqual([small, big]);
        });

        it("updates the mark when a watched group changes size", () => {
            const group = makeGroup(20, 20);
            document.body.appendChild(group);
            observeTranslationGroupSizes(document.body);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);

            setSize(group, kBigWidth, kBigHeight);
            FakeResizeObserver.instances[0].reportResize([group]);

            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });

        it("uses one observer for groups added in a later pass", () => {
            const first = makeGroup(20, 20);
            document.body.appendChild(first);
            observeTranslationGroupSizes(document.body);

            const later = makeGroup(20, 20);
            document.body.appendChild(later);
            observeTranslationGroupSizes(later);

            expect(FakeResizeObserver.instances.length).toBe(1);
            expect(FakeResizeObserver.instances[0].observed).toEqual([
                first,
                later,
            ]);
        });
    });

    describe("stopObservingTranslationGroupSizes", () => {
        it("disconnects and takes the mark off every group", () => {
            const group = makeGroup(20, 20);
            document.body.appendChild(group);
            observeTranslationGroupSizes(document.body);
            const observer = FakeResizeObserver.instances[0];
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(true);

            stopObservingTranslationGroupSizes(document.body);

            expect(observer.disconnected).toBe(true);
            expect(
                group.classList.contains(kTooSmallForInBoxAffordancesClass),
            ).toBe(false);
        });
    });
});
