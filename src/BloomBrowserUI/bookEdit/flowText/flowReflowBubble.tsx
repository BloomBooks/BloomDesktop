// The panel a chained group shows while a refit of its chain waits to be run.
//
// Text never moves across pages on its own. A pass that finds the pages after this one need
// refitting only records that with Bloom (flowText/pendingWalks), and the user decides when the
// work happens: now, with the button here, or whenever they change pages, if the checkbox here
// says so.
//
// The panel belongs to the family of bubbles that sit in the dark area to the right of the page,
// and it is drawn in the source bubbles' own palette. It is NOT a qtip, although those bubbles
// are. Both jquery.qtip.js and jquery.qtipSecondary.js keep their one API object per element
// under the same `qtip` jQuery data key, and a second bind to an element that already has one
// destroys the first (see `init` in lib/jquery.qtip.js): a bubble put on a group with a source
// bubble would take the source bubble away.
//
// It goes in #page-scaling-container, which is where the source-bubble qtips are placed as well
// (bloomQtipUtils.qtipZoomContainer). That container carries the page zoom, so the panel scales
// with the page, and it is outside the page's own content, so it cannot reach the saved page or
// disturb CKEditor.
//
// This module must not import flowTrigger: the trigger calls into here, and the two importing
// each other is the cycle the comment on FlowPassRunner is about. The trigger hands over the one
// thing here that needs it, the wait for this page to finish its own moves, through
// setFlowSettleWaiter.

import { css } from "@emotion/react";
import { Button, Checkbox, FormControlLabel } from "@mui/material";
import * as React from "react";

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { renderRootSync, unmountRoot } from "../../utils/reactRender";
import { waitForBoundaryQueue } from "./flowBoundaryClient";
import {
    kAutoPagesEnglish,
    kAutoPagesL10nId,
    kAutoPagesTestId,
    kChainedGroupSelector,
    kFlowChainAttr,
    kReflowBubbleClass,
    kReflowBubbleTestId,
    kReflowNowEnglish,
    kReflowNowL10nId,
    kReflowNowTestId,
    kReflowOnPageChangeEnglish,
    kReflowOnPageChangeL10nId,
    kReflowOnPageChangeTestId,
    kReflowPendingEnglish,
    kReflowPendingL10nId,
} from "./flowConstants";
import {
    getAutoPages,
    getPendingWalks,
    getReflowOnPageChange,
    postAutoPages,
    postReflowNow,
    postReflowOnPageChange,
} from "./flowReflowClient";

const kPageSelector = ".bloom-page";
const kTranslationGroupSelector = ".bloom-translationGroup";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kBubbleSelector = `.${kReflowBubbleClass}`;
// Where the source bubbles are placed, and so where this panel is placed: the element that
// carries the page zoom.
const kScalingContainerSelector = "#page-scaling-container";

// How far clear of the page's right edge the panel sits. It is the width of the source bubbles'
// own tip (BloomSourceBubbles.MakeSourceBubblesIntoQtips, style.tip.width), so that the two
// stand off the page by the same amount and the arrows are the same length.
const kGapFromPage = 10;
// How far below a source bubble the panel sits when the group is too short for the panel's
// bottom to clear it.
const kGapFromSourceBubble = 6;
// What a source bubble is usually tall, for the case where the group says it has one but there
// is nothing to measure. Erring high keeps the panel clear of it.
const kTypicalSourceBubbleHeight = 160;

// The panel of each group that has one. It is not a child of its group -- it lives beside the
// page -- so the link between the two is held here.
const bubbleByGroup = new Map<HTMLElement, HTMLElement>();

// The wait for the page being edited to finish moving its own text, which the trigger owns.
// Undefined outside the editor, where there is no pass to wait for.
let settleWaiter: (() => Promise<void>) | undefined;

// One refresh runs at a time: the events that ask for one arrive in pairs (the browser knows it
// has just made a walk pending, and Bloom says so over the websocket), and two reads in flight
// would each write the bubbles from its own answer.
let refreshing = false;
let wantedAgain = false;
let refreshTail: Promise<void> = Promise.resolve();

// The window whose resize we are watching, while any panel is up. A resize moves the page and
// changes its zoom, and the panel is placed against both.
let watchedWindow: Window | undefined;

/**
 * Hand over the wait for this page to finish its own moves. The trigger calls this as it takes
 * the page up and again with undefined as it lets it go.
 */
export function setFlowSettleWaiter(
    waiter: (() => Promise<void>) | undefined,
): void {
    settleWaiter = waiter;
}

/**
 * Put the panel beside every chained group on this page whose own chain has a refit waiting,
 * and take it away from every other group.
 *
 * Nothing is remembered between pages: which chains are waiting is Bloom's to say, and the
 * answer is read afresh each time.
 *
 * Settles when the panels are up to date, including a refresh asked for while this one ran.
 */
export function refreshReflowBubbles(
    root: ParentNode = document,
): Promise<void> {
    if (refreshing) {
        wantedAgain = true;
        return refreshTail;
    }

    refreshing = true;
    refreshTail = updateBubbles(root).then(() => {
        refreshing = false;
        if (wantedAgain) {
            wantedAgain = false;
            return refreshReflowBubbles(root);
        }

        return undefined;
    });
    return refreshTail;
}

/** Take every panel away, whether or not its chain still has a refit waiting. */
export function removeReflowBubbles(root: ParentNode): void {
    Array.from(bubbleByGroup.values()).forEach(removeBubble);
    bubbleByGroup.clear();
    // A panel whose group has gone is no longer in the map, and the panel is not inside the
    // page, so the search covers the whole document rather than what was handed in.
    const doc = root instanceof Document ? root : (root as Node).ownerDocument;
    Array.from(
        (doc ?? root).querySelectorAll<HTMLElement>(kBubbleSelector),
    ).forEach(removeBubble);
    stopWatchingForResize();
}

/** The panel beside this group, if it has one. */
export function getReflowBubbleFor(
    group: HTMLElement,
): HTMLElement | undefined {
    return bubbleByGroup.get(group);
}

async function updateBubbles(root: ParentNode): Promise<void> {
    const status = await getPendingWalks();
    if (!status) {
        // Bloom could not be asked. What the page shows is what it last said, which is better
        // than claiming nothing is waiting.
        return;
    }

    const waitingChains = new Set(status.pending ? status.chainIds : []);
    // The reply usually carries the settings; when it does not, each is a question of its own.
    const onPageChange =
        status.reflowOnPageChange ?? (await getReflowOnPageChange());
    const autoPages = status.autoPages ?? (await getAutoPages());

    const shown = new Set<HTMLElement>();
    getGroupsOnPages(root).forEach((group) => {
        const chainId = group.getAttribute(kFlowChainAttr);
        if (chainId && waitingChains.has(chainId)) {
            showBubble(group, onPageChange, autoPages);
            shown.add(group);
        }
    });

    // A panel whose group is not one of those has nothing left to be about: its chain's refit
    // has run, or the group has left its chain, or its page is no longer the one being edited.
    Array.from(bubbleByGroup.keys()).forEach((group) => {
        if (!shown.has(group)) {
            takeBubbleFrom(group);
        }
    });
}

/** The chained groups of every page under `root` that have a box the reader can see. */
function getGroupsOnPages(root: ParentNode): HTMLElement[] {
    const groups: HTMLElement[] = [];
    getPages(root).forEach((page) => {
        page.querySelectorAll<HTMLElement>(
            `${kChainedGroupSelector} > ${kVisibleEditableSelector}`,
        ).forEach((editable) => {
            const group = editable.closest<HTMLElement>(
                kTranslationGroupSelector,
            );
            if (group && !groups.includes(group)) {
                groups.push(group);
            }
        });
    });
    return groups;
}

function showBubble(
    group: HTMLElement,
    onPageChange: boolean,
    autoPages: boolean,
): void {
    const page = group.closest<HTMLElement>(kPageSelector);
    if (!page) {
        return;
    }

    const existing = bubbleByGroup.get(group);
    const bubble = existing ?? makeBubble(group.ownerDocument);
    if (!existing) {
        getBubbleHome(page).appendChild(bubble);
        bubbleByGroup.set(group, bubble);
    }

    // The panel is only ever up while a refit is waiting, so this always says so. The end-to-end
    // helpers pick the panel out by it.
    renderRootSync(
        <ReflowBubble
            reflowOnPageChange={onPageChange}
            autoPages={autoPages}
            onReflowNow={reflowNow}
            onReflowOnPageChangeChanged={postReflowOnPageChange}
            onAutoPagesChanged={postAutoPages}
        />,
        bubble,
    );

    // renderRootSync has committed, so the panel has the height its placement is worked out
    // from.
    placeBubble(group, bubble);
    startWatchingForResize(group.ownerDocument.defaultView ?? undefined);
}

function makeBubble(document: Document): HTMLElement {
    const bubble = document.createElement("div");
    bubble.className = `bloom-ui ${kReflowBubbleClass}`;
    bubble.setAttribute("contenteditable", "false");
    bubble.setAttribute("data-testid", kReflowBubbleTestId);
    return bubble;
}

/**
 * Where the panel is put: beside the page, in the element that carries the page zoom, which is
 * where the source bubbles go too. A document without that container, such as a test's, gets
 * the page's own parent.
 */
function getBubbleHome(page: HTMLElement): HTMLElement {
    return (
        page.ownerDocument.querySelector<HTMLElement>(
            kScalingContainerSelector,
        ) ??
        page.parentElement ??
        page.ownerDocument.body
    );
}

function takeBubbleFrom(group: HTMLElement): void {
    const bubble = bubbleByGroup.get(group);
    if (!bubble) {
        return;
    }

    bubbleByGroup.delete(group);
    removeBubble(bubble);
    if (!bubbleByGroup.size) {
        stopWatchingForResize();
    }
}

function removeBubble(bubble: HTMLElement): void {
    unmountRoot(bubble);
    bubble.remove();
}

/**
 * Put the panel to the right of the page, beside the box it belongs to.
 *
 * The panel is in the page's zoom container and positioned absolutely, so its left and top are
 * that container's own coordinates, which the page's zoom multiplies. Every measurement here is
 * a viewport rectangle, so each one is turned into a container coordinate the way
 * audioRecording's icon marker does it: take the container's own corner off, then divide by the
 * zoom the container's transform carries.
 */
function placeBubble(group: HTMLElement, bubble: HTMLElement): void {
    const page = group.closest<HTMLElement>(kPageSelector);
    const container = bubble.parentElement;
    if (!page || !container) {
        return;
    }

    const containerRect = container.getBoundingClientRect();
    const pageRect = page.getBoundingClientRect();
    const groupRect = group.getBoundingClientRect();
    const placement = computeReflowBubblePlacement({
        containerLeft: containerRect.left,
        containerTop: containerRect.top,
        zoom: getZoom(container),
        pageRight: pageRect.right,
        groupTop: groupRect.top,
        groupBottom: groupRect.bottom,
        bubbleHeight: bubble.offsetHeight,
        sourceBubbleHeight: getSourceBubbleHeight(group),
    });
    bubble.style.left = `${placement.left}px`;
    bubble.style.top = `${placement.top}px`;
}

/** Where a panel sits, in the coordinates of the container it is placed in. */
export interface IReflowBubblePlacement {
    left: number;
    top: number;
}

/**
 * Where the panel goes.
 *
 * Across: clear of the page's right edge by the same gap the source bubbles keep, so the panel
 * is in the dark area beside the page rather than over the text.
 *
 * Down: its BOTTOM lined up with the bottom of the group it belongs to. A source bubble hangs
 * from the TOP of the same group, so on any group taller than the two of them together that
 * alone keeps the panel clear of it. On a shorter group it would not, so the panel goes below
 * where the source bubble ends instead. A group with no source bubble is only ever
 * bottom-aligned; pass 0 for sourceBubbleHeight to say there is none.
 */
export function computeReflowBubblePlacement(args: {
    /** The container's own top left corner, in viewport pixels. */
    containerLeft: number;
    containerTop: number;
    /** The zoom the container's transform carries; its coordinates are multiplied by this. */
    zoom: number;
    /** The page's right edge, and the group's top and bottom, in viewport pixels. */
    pageRight: number;
    groupTop: number;
    groupBottom: number;
    /** The panel's own height, in the container's coordinates. */
    bubbleHeight: number;
    /** The height of the source bubble on this group, or 0 when it has none. */
    sourceBubbleHeight: number;
}): IReflowBubblePlacement {
    const zoom = args.zoom || 1;
    const left = (args.pageRight - args.containerLeft) / zoom + kGapFromPage;
    const groupTop = (args.groupTop - args.containerTop) / zoom;
    const groupBottom = (args.groupBottom - args.containerTop) / zoom;
    const bottomAligned = groupBottom - args.bubbleHeight;
    if (!args.sourceBubbleHeight) {
        return { left, top: bottomAligned };
    }

    const belowSourceBubble =
        groupTop + args.sourceBubbleHeight + kGapFromSourceBubble;
    return { left, top: Math.max(bottomAligned, belowSourceBubble) };
}

/**
 * How tall the source bubble on this group is, or 0 when it has none.
 *
 * A group's aria-describedby is saved with the page and can name a tooltip that no longer
 * exists, and a group can be marked as having a bubble before that bubble has been rendered
 * (BloomHintBubbles says as much), so a typical height stands in when the group says it has one
 * and there is nothing to measure.
 */
function getSourceBubbleHeight(group: HTMLElement): number {
    if (!group.hasAttribute("data-hasqtip")) {
        return 0;
    }

    const tipId = group.getAttribute("aria-describedby");
    const tip = tipId ? group.ownerDocument.getElementById(tipId) : undefined;
    return tip?.offsetHeight || kTypicalSourceBubbleHeight;
}

/** The zoom the page's container carries, or 1 when it carries none. */
function getZoom(container: HTMLElement): number {
    const view = container.ownerDocument.defaultView;
    const transform = view?.getComputedStyle(container).transform;
    if (!view || !transform || transform === "none") {
        return 1;
    }

    // jsdom has no DOMMatrix, and a transform we cannot read is one we cannot allow for.
    if (!view.DOMMatrix) {
        return 1;
    }

    try {
        return new view.DOMMatrix(transform).a || 1;
    } catch {
        return 1;
    }
}

function startWatchingForResize(view: Window | undefined): void {
    if (!view || watchedWindow === view) {
        return;
    }

    stopWatchingForResize();
    watchedWindow = view;
    view.addEventListener("resize", placeEveryBubble);
}

function stopWatchingForResize(): void {
    watchedWindow?.removeEventListener("resize", placeEveryBubble);
    watchedWindow = undefined;
}

/** Put every panel back where it belongs, because the page has moved or changed size. */
function placeEveryBubble(): void {
    bubbleByGroup.forEach((bubble, group) => {
        if (group.isConnected && bubble.isConnected) {
            placeBubble(group, bubble);
        }
    });
}

/**
 * Run the waiting refits.
 *
 * This page's own moves go first. A refit that started while the browser still had text to hand
 * to the next page would refuse that move, and the page being edited would keep text that no
 * longer fits it; and the requests the page is about to make are part of what the refit has to
 * do. So the wait covers both the trigger's pass and the round trips it has asked for.
 */
async function reflowNow(): Promise<void> {
    if (settleWaiter) {
        await settleWaiter();
    }

    await waitForBoundaryQueue();
    await postReflowNow();
}

// The source bubbles' own palette (sourceBubbles.less), so that the controls in the panel are
// drawn in the colours of the bubble they sit in rather than MUI's blue.
const kBubbleBorder = "#96668f";
const kBubbleText = "black";

/** One of the panel's settings: a checkbox drawn in the bubble's palette. */
const SettingCheckbox: React.FunctionComponent<{
    testId: string;
    label: string;
    checked: boolean;
    onChanged: (value: boolean) => void;
}> = (props) => (
    <FormControlLabel
        css={css`
            margin: 0;
            .MuiFormControlLabel-label {
                font-family: inherit;
                font-size: inherit;
                line-height: 1.3;
                color: ${kBubbleText};
                // The panel is sized to its content (editMode.less), and this is what
                // makes the label one line for it to be sized to.
                white-space: nowrap;
            }
        `}
        control={
            <Checkbox
                size="small"
                // On the input itself, not on the wrapper the checkbox draws, so that
                // what a test finds is the thing it can tick.
                inputProps={{ "data-testid": props.testId }}
                checked={props.checked}
                onChange={(event) => props.onChanged(event.target.checked)}
                css={css`
                    padding: 2px;
                    margin-right: 4px;
                    color: ${kBubbleBorder};
                    &.Mui-checked {
                        color: ${kBubbleBorder};
                    }
                `}
            />
        }
        label={props.label}
    />
);

/**
 * What the panel says: that a refit is waiting, a way to have every page change run it, whether a
 * refit may add and remove pages, and a button to run it now.
 */
export const ReflowBubble: React.FunctionComponent<{
    reflowOnPageChange: boolean;
    autoPages: boolean;
    onReflowOnPageChangeChanged: (value: boolean) => Promise<void>;
    onAutoPagesChanged: (value: boolean) => Promise<void>;
    onReflowNow: () => Promise<void>;
}> = (props) => {
    const [onPageChange, setOnPageChange] = React.useState(
        props.reflowOnPageChange,
    );
    const [autoPages, setAutoPages] = React.useState(props.autoPages);
    const [running, setRunning] = React.useState(false);
    const pendingLabel = useFlowLabel(
        kReflowPendingL10nId,
        kReflowPendingEnglish,
    );
    const onPageChangeLabel = useFlowLabel(
        kReflowOnPageChangeL10nId,
        kReflowOnPageChangeEnglish,
    );
    const autoPagesLabel = useFlowLabel(kAutoPagesL10nId, kAutoPagesEnglish);
    const reflowNowLabel = useFlowLabel(kReflowNowL10nId, kReflowNowEnglish);

    // The panel is rendered again each time Bloom is asked what is waiting, and both settings are
    // the book's rather than this panel's: another page's panel can have changed them.
    React.useEffect(
        () => setOnPageChange(props.reflowOnPageChange),
        [props.reflowOnPageChange],
    );
    React.useEffect(() => setAutoPages(props.autoPages), [props.autoPages]);

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                align-items: flex-start;
                gap: 2px;
            `}
        >
            <div
                css={css`
                    font-weight: bold;
                `}
            >
                {pendingLabel}
            </div>
            <SettingCheckbox
                testId={kReflowOnPageChangeTestId}
                label={onPageChangeLabel}
                checked={onPageChange}
                onChanged={(value) => {
                    setOnPageChange(value);
                    void props.onReflowOnPageChangeChanged(value);
                }}
            />
            <SettingCheckbox
                testId={kAutoPagesTestId}
                label={autoPagesLabel}
                checked={autoPages}
                onChanged={(value) => {
                    setAutoPages(value);
                    void props.onAutoPagesChanged(value);
                }}
            />
            <Button
                size="small"
                variant="contained"
                data-testid={kReflowNowTestId}
                disabled={running}
                onClick={() => {
                    setRunning(true);
                    void props.onReflowNow().finally(() => setRunning(false));
                }}
                css={css`
                    margin-top: 3px;
                    font-family: inherit;
                    font-size: inherit;
                    text-transform: none;
                    padding: 1px 10px;
                    min-width: 0;
                    box-shadow: none;
                    background-color: ${kBubbleBorder};
                    color: white;
                    &:hover {
                        background-color: ${kBubbleBorder};
                        filter: brightness(0.9);
                        box-shadow: none;
                    }
                `}
            >
                {reflowNowLabel}
            </Button>
        </div>
    );
};

/**
 * The localized words, with the English standing in until the answer arrives. The localization
 * manager answers asynchronously and there is nothing else to hang the call on, so this is a
 * useEffect; a unit test has no manager, and the English is right there, so a failure to
 * localize must not stop the panel appearing.
 */
function useFlowLabel(l10nId: string, english: string): string {
    const [text, setText] = React.useState(english);
    React.useEffect(() => {
        let wanted = true;
        try {
            theOneLocalizationManager
                .asyncGetText(l10nId, english, "")
                .done((answer: string) => {
                    if (wanted && answer) {
                        setText(answer);
                    }
                });
        } catch {
            // Keep the English.
        }

        return () => {
            wanted = false;
        };
    }, [l10nId, english]);
    return text;
}

function getPages(root: ParentNode): HTMLElement[] {
    if (root instanceof HTMLElement && root.matches(kPageSelector)) {
        return [root];
    }

    return Array.from(root.querySelectorAll<HTMLElement>(kPageSelector));
}
