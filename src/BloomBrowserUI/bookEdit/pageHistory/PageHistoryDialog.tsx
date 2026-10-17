// The Show Page History dialog, a developer's tool opened from the top bar's right-click menu: a
// floating window in the root window with two tabs. The Stack tab shows the current page's undo
// and redo steps, around a marker for where the page is now, and what last cleared the history.
// The Log tab lists what the page history has done, from the recorder (pageHistoryRecorder.ts),
// across page changes; while the dialog is open, detailed logging is on, so the log has every
// transaction and change as well.
// It is not modal, and it never takes the focus, so the user can go on typing in the page while it
// is open: every press in it, except on its resize grip, has its default (focusing) prevented, and
// pageHistoryInput.ts leaves its events alone (kIgnoredByPageHistoryInputSelector), so using it
// makes no undo step and never ends the history.

import * as React from "react";
import * as ReactDOM from "react-dom";
import { css } from "@emotion/react";
import Draggable from "react-draggable";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import CloseIcon from "@mui/icons-material/Close";
import UndoIcon from "@mui/icons-material/Undo";
import RedoIcon from "@mui/icons-material/Redo";
import {
    formatPageHistoryClear,
    formatPageHistoryEventTime,
    formatPageHistoryStack,
    kPageHistoryChangeLayoutModeNote,
    kPageHistoryEventLabels,
    kPageHistoryNoStack,
    kPageHistoryStackMarker,
    kPageHistoryStepInProgress,
    pageHistoryRecorder,
    type PageHistoryEvent,
    type PageHistoryEventKind,
    type PageHistoryStack,
} from "./pageHistoryRecorder";
import { kIgnoredByPageHistoryInputSelector } from "./pageHistoryInput";
import { postJson } from "../../utils/bloomApi";
import { useMountEffect } from "../../utils/useMountEffect";
import {
    kBloomBlue,
    kDisabledControlGray,
    kUiFontStack,
} from "../../bloomMaterialUITheme";

// Where the dialog first appears, from the top of the window.
const kTop = 60;

/**
 * How many of the newest events the Log tab shows. Rendering every event that the recorder keeps
 * would slow typing in the page while the dialog is open; Copy copies them all.
 */
export const kMaxLogRowsShown = 1000;

// The attribute named by kIgnoredByPageHistoryInputSelector, as a JSX prop.
const kIgnoredByInputProps = {
    [kIgnoredByPageHistoryInputSelector.slice(1, -1)]: "",
};

// The quiet palette of the dialog, as in Bloom's own dialogs. The theme's primary colour
// (kBloomBlue) is kept for the selected tab, the buttons and the "you are here" marker.
const kTextColor = "#333";
const kCaptionColor = "#777";
const kIconColor = "#888";
// The redo steps, below the marker, are lighter, so that the future reads as future.
const kFutureColor = "#999";
const kRuleColor = "#ddd";
const kTitleBackground = "#f5f5f5";

// The colours of the labels of the kinds of row that must stand out, muted; their text says
// what they are too.
const kLabelColors: Partial<Record<PageHistoryEventKind, string>> = {
    undo: "#4f6d8a",
    redo: "#5b7f63",
    rollback: "#a0574f",
    refused: "#9a7a3c",
    cleared: "#9a7a3c",
};

// The kinds that are recorded only while the dialog is open, and the page becoming ready, which
// are shown in the caption colour.
const kQuietKinds = new Set<PageHistoryEventKind>([
    "transaction",
    "input",
    "change",
    "pageReady",
]);

// What shows a step's description or a log row's text.
const kMonospace = `Consolas, "Courier New", monospace`;

type PageHistoryDialogTab = "stack" | "log";

/** For useSyncExternalStore; outside the component so that it does not resubscribe each render. */
function subscribeToRecorder(listener: () => void): () => void {
    return pageHistoryRecorder.subscribe(listener);
}

// The size of the corner where the browser draws the grip of `resize: both`.
const kResizeGripSize = 16;

/**
 * True for a press on the dialog's resize grip, whose default action, resizing, must not be
 * prevented.
 */
function isOnResizeGrip(event: MouseEvent, dialog: HTMLElement): boolean {
    const rect = dialog.getBoundingClientRect();
    return (
        event.clientX > rect.right - kResizeGripSize &&
        event.clientY > rect.bottom - kResizeGripSize
    );
}

/** For useSyncExternalStore: changes whenever the recorder does. */
function getRecorderChangeCount(): number {
    return pageHistoryRecorder.getChangeCount();
}

export const PageHistoryDialog: React.FunctionComponent<{
    onClose: () => void;
}> = (props) => {
    React.useSyncExternalStore(subscribeToRecorder, getRecorderChangeCount);
    const events = pageHistoryRecorder.getEvents();
    const stack = pageHistoryRecorder.getStack();
    const latestClear = pageHistoryRecorder.getLatestClear();
    const [tab, setTab] = React.useState<PageHistoryDialogTab>("stack");
    const dialogRef = React.useRef<HTMLDivElement>(null);
    const listRef = React.useRef<HTMLDivElement>(null);
    // False once the user scrolls up from the newest event, true again at the bottom.
    const followsNewest = React.useRef(true);
    // The stack that the Stack tab last scrolled to its marker for.
    const stackScrolledTo = React.useRef<PageHistoryStack>();
    const [copiedMessage, setCopiedMessage] = React.useState<string>();

    // An effect because detailed logging is state outside React, in the recorder and in each page
    // frame's history, that follows the dialog's lifetime: on while it is open, off once it closes.
    useMountEffect(() => {
        pageHistoryRecorder.setDetailed(true);
        return () => pageHistoryRecorder.setDetailed(false);
    });

    // An effect because it keeps the list's scroll position, which is DOM state outside React, in
    // step with what was just rendered, before the browser paints: the log shows the newest event
    // unless the user has scrolled up; the stack brings its marker into view when the stack changes.
    React.useLayoutEffect(() => {
        const list = listRef.current;
        if (!list) return;
        if (tab === "log") {
            stackScrolledTo.current = undefined;
            if (followsNewest.current) list.scrollTop = list.scrollHeight;
            return;
        }
        if (stack === stackScrolledTo.current) return;
        stackScrolledTo.current = stack;
        const marker = list.querySelector<HTMLElement>(
            ".page-history-stack-marker",
        );
        if (!marker) return;
        const isInView =
            marker.offsetTop >= list.scrollTop &&
            marker.offsetTop + marker.offsetHeight <=
                list.scrollTop + list.clientHeight;
        if (!isInView)
            list.scrollTop = marker.offsetTop - list.clientHeight / 2;
    });

    const copy = () => {
        let text: string;
        if (tab === "stack") {
            text = formatPageHistoryStack(stack, latestClear);
            setCopiedMessage("Copied the stack.");
        } else {
            text = pageHistoryRecorder.asText();
            setCopiedMessage("Copied the whole log.");
        }
        // common/clipboardText is on kRequestsThatKeepThePageHistory, for a WebView2 that
        // refuses the clipboard to a document that does not have the focus.
        navigator.clipboard
            .writeText(text)
            .catch(() => postJson("common/clipboardText", { text }));
    };

    const omittedCount = Math.max(0, events.length - kMaxLogRowsShown);

    const dialog = (
        <Draggable
            nodeRef={dialogRef}
            handle=".page-history-dialog-title"
            cancel="button, label"
            bounds={{ top: -kTop }}
            // Keep the focus where it is, in the page, so that typing goes on there. This is on
            // Draggable, not on the div: Draggable replaces its child's own onMouseDown.
            onMouseDown={(e) => {
                if (!isOnResizeGrip(e, dialogRef.current!)) e.preventDefault();
            }}
        >
            <div
                ref={dialogRef}
                className="page-history-dialog"
                {...kIgnoredByInputProps}
                css={css`
                    position: fixed;
                    top: ${kTop}px;
                    right: 16px;
                    width: 760px;
                    height: 440px;
                    min-width: 360px;
                    min-height: 200px;
                    max-width: calc(100vw - 32px);
                    resize: both;
                    overflow: hidden;
                    z-index: 1290; // above the page, below MUI's modal dialogs and menus
                    display: flex;
                    flex-direction: column;
                    background: white;
                    color: ${kTextColor};
                    border: 1px solid ${kDisabledControlGray};
                    border-radius: 4px;
                    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
                    font-family: ${kUiFontStack};
                    font-size: 12px;
                `}
            >
                <div
                    className="page-history-dialog-title"
                    css={css`
                        display: flex;
                        align-items: center;
                        gap: 8px;
                        padding: 2px 4px 2px 10px;
                        background: ${kTitleBackground};
                        color: ${kTextColor};
                        border-bottom: 1px solid ${kRuleColor};
                        cursor: move;
                        user-select: none;
                    `}
                >
                    <span
                        css={css`
                            font-weight: 500;
                            flex-grow: 1;
                        `}
                    >
                        Page History
                    </span>
                    <IconButton
                        size="small"
                        aria-label="Close"
                        onClick={props.onClose}
                        css={css`
                            color: ${kTextColor};
                        `}
                    >
                        <CloseIcon fontSize="small" />
                    </IconButton>
                </div>
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        gap: 8px;
                        padding: 0 8px;
                        border-bottom: 1px solid ${kRuleColor};
                    `}
                >
                    <Tabs
                        value={tab}
                        onChange={(_e, value: PageHistoryDialogTab) => {
                            setTab(value);
                            setCopiedMessage(undefined);
                        }}
                        css={css`
                            min-height: 32px;
                            .MuiTabs-indicator {
                                background-color: ${kBloomBlue};
                            }
                            .MuiTab-root {
                                min-height: 32px;
                                min-width: 0;
                                padding: 4px 12px;
                                text-transform: none;
                                color: ${kCaptionColor};
                            }
                            .MuiTab-root.Mui-selected {
                                color: ${kTextColor};
                            }
                        `}
                    >
                        <Tab value="stack" label="Stack" />
                        <Tab value="log" label="Log" />
                    </Tabs>
                    <span
                        css={css`
                            flex-grow: 1;
                            text-align: right;
                            color: ${kCaptionColor};
                        `}
                    >
                        {copiedMessage}
                    </span>
                    <Button size="small" variant="outlined" onClick={copy}>
                        Copy
                    </Button>
                    {tab === "log" && (
                        <Button
                            size="small"
                            variant="outlined"
                            onClick={() => {
                                pageHistoryRecorder.clear();
                                setCopiedMessage(undefined);
                            }}
                        >
                            Clear
                        </Button>
                    )}
                </div>
                <div
                    ref={listRef}
                    className={
                        tab === "log"
                            ? "page-history-dialog-list"
                            : "page-history-dialog-stack"
                    }
                    onScroll={(e) => {
                        if (tab !== "log") return;
                        const list = e.currentTarget;
                        followsNewest.current =
                            list.scrollHeight -
                                list.scrollTop -
                                list.clientHeight <
                            8;
                    }}
                    css={css`
                        flex-grow: 1;
                        overflow-y: auto;
                        position: relative; // the offsetParent of the stack's marker
                        padding: 2px 0;
                    `}
                >
                    {tab === "log" ? (
                        <>
                            {omittedCount > 0 && (
                                <div
                                    className="page-history-log-omitted"
                                    css={css`
                                        padding: 1px 6px;
                                        color: ${kCaptionColor};
                                        font-style: italic;
                                    `}
                                >
                                    Older rows are not shown here; Copy includes
                                    them all.
                                </div>
                            )}
                            {events.slice(omittedCount).map((event) => (
                                <PageHistoryEventRow
                                    key={event.id}
                                    event={event}
                                />
                            ))}
                        </>
                    ) : (
                        <PageHistoryStackView
                            stack={stack}
                            latestClear={latestClear}
                        />
                    )}
                </div>
            </div>
        </Draggable>
    );
    return ReactDOM.createPortal(dialog, document.body);
};

// The layout of every row of the Stack tab.
const kStackRowCss = css`
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 1px 8px;
    white-space: pre-wrap;
    word-break: break-all;
`;

/**
 * The Stack tab: the latest clear if there has been one since the page loaded, the undo
 * steps oldest first, a row for a step in progress if there is one, the marker, and the redo
 * steps, the one that Redo would redo first.
 */
const PageHistoryStackView: React.FunctionComponent<{
    stack: PageHistoryStack | undefined;
    latestClear: PageHistoryEvent | undefined;
}> = (props) => {
    const clearRow = props.latestClear && (
        <div className="page-history-stack-clear" css={kStackRowCss}>
            {formatPageHistoryClear(props.latestClear)}
        </div>
    );
    if (!props.stack)
        return (
            <>
                {clearRow}
                <div
                    className="page-history-stack-none"
                    css={css`
                        ${kStackRowCss}
                        color: ${kCaptionColor};
                    `}
                >
                    {kPageHistoryNoStack}
                </div>
            </>
        );
    return (
        <>
            {clearRow}
            {props.stack.isInChangeLayoutMode && (
                <div
                    className="page-history-stack-note"
                    css={css`
                        ${kStackRowCss}
                        color: ${kCaptionColor};
                        font-style: italic;
                    `}
                >
                    {kPageHistoryChangeLayoutModeNote}
                </div>
            )}
            {props.stack.undo.map((step, i) => (
                <div
                    key={`undo-${i}`}
                    className="page-history-stack-row page-history-stack-undo"
                    css={css`
                        ${kStackRowCss}
                        font-family: ${kMonospace};
                        color: ${kTextColor};
                        svg {
                            color: ${kIconColor};
                        }
                    `}
                >
                    <UndoIcon sx={{ fontSize: 14 }} />
                    {step}
                </div>
            ))}
            {props.stack.hasOpenTransaction && (
                <div
                    className="page-history-stack-row page-history-stack-in-progress"
                    css={css`
                        ${kStackRowCss}
                        color: ${kCaptionColor};
                        font-style: italic;
                    `}
                >
                    {kPageHistoryStepInProgress}
                </div>
            )}
            <div
                className="page-history-stack-marker"
                css={css`
                    ${kStackRowCss}
                    margin: 2px 0;
                    font-size: 11px;
                    color: ${kBloomBlue};
                    &::after {
                        content: "";
                        flex-grow: 1;
                        border-top: 1px solid ${kBloomBlue};
                    }
                `}
            >
                {kPageHistoryStackMarker}
            </div>
            {props.stack.redo.map((step, i) => (
                <div
                    key={`redo-${i}`}
                    className="page-history-stack-row page-history-stack-redo"
                    css={css`
                        ${kStackRowCss}
                        font-family: ${kMonospace};
                        color: ${kFutureColor};
                    `}
                >
                    <RedoIcon sx={{ fontSize: 14 }} />
                    {step}
                </div>
            ))}
        </>
    );
};

/** One event: its time, its label (with an icon for undo and redo), and its text. */
const PageHistoryEventRow: React.FunctionComponent<{
    event: PageHistoryEvent;
}> = React.memo((props) => {
    const kind = props.event.kind;
    const labelColor = kLabelColors[kind];
    const isQuiet = kQuietKinds.has(kind);
    return (
        <div
            className={`page-history-row page-history-row-${kind}`}
            css={css`
                display: flex;
                gap: 8px;
                padding: 1px 6px;
                white-space: pre-wrap;
                word-break: break-all;
                font-family: ${kMonospace};
                color: ${isQuiet ? kCaptionColor : kTextColor};
                ${kind === "pageLoad"
                    ? `border-top: 1px solid ${kRuleColor}; margin-top: 2px; color: ${kCaptionColor};`
                    : ""}
            `}
        >
            <span
                css={css`
                    flex: none;
                    width: 64px;
                    text-align: right;
                    color: ${kCaptionColor};
                `}
            >
                {formatPageHistoryEventTime(props.event)}
            </span>
            <span
                css={css`
                    flex: none;
                    width: 84px;
                    display: flex;
                    align-items: center;
                    gap: 2px;
                    ${labelColor
                        ? `color: ${labelColor}; font-weight: 600;`
                        : ""}
                `}
            >
                {kind === "undo" && <UndoIcon sx={{ fontSize: 14 }} />}
                {kind === "redo" && <RedoIcon sx={{ fontSize: 14 }} />}
                {kPageHistoryEventLabels[kind]}
            </span>
            <span>{props.event.text}</span>
        </div>
    );
});
PageHistoryEventRow.displayName = "PageHistoryEventRow";
