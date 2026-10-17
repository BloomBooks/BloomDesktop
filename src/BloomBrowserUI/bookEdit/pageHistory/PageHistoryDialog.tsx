// The Show Page History dialog, a developer's tool opened from the top bar's right-click menu: a
// floating window in the root window that lists what the page history has done, from the
// recorder (pageHistoryRecorder.ts), across page changes. It is not modal, and it never takes the
// focus, so the user can go on typing in the page while it is open: every press in it has its
// default (focusing) prevented, and pageHistoryInput.ts leaves its events alone
// (kIgnoredByPageHistoryInputSelector), so using it makes no undo step and never ends the history.

import * as React from "react";
import * as ReactDOM from "react-dom";
import { css } from "@emotion/react";
import Draggable from "react-draggable";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import CloseIcon from "@mui/icons-material/Close";
import UndoIcon from "@mui/icons-material/Undo";
import RedoIcon from "@mui/icons-material/Redo";
import {
    formatPageHistoryEventTime,
    kMaxPageHistoryEvents,
    kPageHistoryEventLabels,
    pageHistoryRecorder,
    type PageHistoryEvent,
    type PageHistoryEventKind,
} from "./pageHistoryRecorder";
import { kIgnoredByPageHistoryInputSelector } from "./pageHistoryInput";
import { postJson } from "../../utils/bloomApi";

// Where the dialog first appears, from the top of the window.
const kTop = 60;

// The attribute named by kIgnoredByPageHistoryInputSelector, as a JSX prop.
const kIgnoredByInputProps = {
    [kIgnoredByPageHistoryInputSelector.slice(1, -1)]: "",
};

// The colours of the kinds of row that must stand out; the rest are plain.
const kRowColors: Partial<
    Record<PageHistoryEventKind, { background: string; text: string }>
> = {
    undo: { background: "#dbeafe", text: "#1e3a8a" },
    redo: { background: "#dcfce7", text: "#14532d" },
    rollback: { background: "#fee2e2", text: "#7f1d1d" },
    refused: { background: "#ffedd5", text: "#7c2d12" },
    cleared: { background: "#ffedd5", text: "#7c2d12" },
    pageLoad: { background: "#e5e7eb", text: "#111827" },
    pageReady: { background: "#f3f4f6", text: "#111827" },
};

// The kinds that only detailed logging records, shown smaller and grey.
const kDetailKinds = new Set<PageHistoryEventKind>([
    "transaction",
    "input",
    "change",
]);

/** For useSyncExternalStore; outside the component so that it does not resubscribe each render. */
function subscribeToRecorder(listener: () => void): () => void {
    return pageHistoryRecorder.subscribe(listener);
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
    const dialogRef = React.useRef<HTMLDivElement>(null);
    const listRef = React.useRef<HTMLDivElement>(null);
    // False once the user scrolls up from the newest event, true again at the bottom.
    const followsNewest = React.useRef(true);
    const [copiedCount, setCopiedCount] = React.useState<number>();

    // An effect because it keeps the list's scroll position, which is DOM state outside React, in
    // step with what was just rendered: after each render, before the browser paints, show the
    // newest event unless the user has scrolled up.
    React.useLayoutEffect(() => {
        const list = listRef.current;
        if (list && followsNewest.current) list.scrollTop = list.scrollHeight;
    });

    const close = () => {
        pageHistoryRecorder.setDetailed(false);
        props.onClose();
    };

    const copy = () => {
        const text = pageHistoryRecorder.asText();
        setCopiedCount(events.length);
        // common/clipboardText is on kRequestsThatKeepThePageHistory, for a WebView2 that
        // refuses the clipboard to a document that does not have the focus.
        navigator.clipboard
            .writeText(text)
            .catch(() => postJson("common/clipboardText", { text }));
    };

    const dialog = (
        <Draggable
            nodeRef={dialogRef}
            handle=".page-history-dialog-title"
            cancel="button, label"
            bounds={{ top: -kTop }}
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
                    color: black;
                    border: 1px solid #9ca3af;
                    border-radius: 4px;
                    box-shadow: 0 6px 24px rgba(0, 0, 0, 0.35);
                    font-family: "Segoe UI", sans-serif;
                    font-size: 12px;
                `}
                // Keep the focus where it is, in the page, so that typing goes on there.
                onMouseDown={(e) => e.preventDefault()}
            >
                <div
                    className="page-history-dialog-title"
                    css={css`
                        display: flex;
                        align-items: center;
                        gap: 8px;
                        padding: 2px 4px 2px 10px;
                        background: #374151;
                        color: white;
                        cursor: move;
                        user-select: none;
                    `}
                >
                    <span
                        css={css`
                            font-weight: bold;
                            flex-grow: 1;
                        `}
                    >
                        Page History
                    </span>
                    <IconButton
                        size="small"
                        aria-label="Close"
                        onClick={close}
                        css={css`
                            color: white;
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
                        padding: 2px 8px;
                        border-bottom: 1px solid #d1d5db;
                    `}
                >
                    <FormControlLabel
                        control={
                            <Checkbox
                                size="small"
                                checked={pageHistoryRecorder.isDetailed}
                                onChange={(e) =>
                                    pageHistoryRecorder.setDetailed(
                                        e.target.checked,
                                    )
                                }
                            />
                        }
                        label="Detailed logging"
                    />
                    <Button size="small" variant="outlined" onClick={copy}>
                        Copy
                    </Button>
                    <Button
                        size="small"
                        variant="outlined"
                        onClick={() => {
                            pageHistoryRecorder.clear();
                            setCopiedCount(undefined);
                        }}
                    >
                        Clear
                    </Button>
                    <span
                        css={css`
                            flex-grow: 1;
                            text-align: right;
                            color: #4b5563;
                        `}
                    >
                        {copiedCount !== undefined &&
                            `Copied ${copiedCount} events. `}
                        {`${events.length} events (the last ${kMaxPageHistoryEvents} are kept)`}
                    </span>
                </div>
                <div
                    ref={listRef}
                    className="page-history-dialog-list"
                    onScroll={(e) => {
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
                        font-family: Consolas, "Courier New", monospace;
                    `}
                >
                    {events.map((event) => (
                        <PageHistoryEventRow key={event.id} event={event} />
                    ))}
                </div>
            </div>
        </Draggable>
    );
    return ReactDOM.createPortal(dialog, document.body);
};

/** One event: its time, its label (with an icon for undo and redo), and its text. */
const PageHistoryEventRow: React.FunctionComponent<{
    event: PageHistoryEvent;
}> = React.memo((props) => {
    const kind = props.event.kind;
    const colors = kRowColors[kind];
    const isDetail = kDetailKinds.has(kind);
    const isStrong =
        kind === "undo" ||
        kind === "redo" ||
        kind === "rollback" ||
        kind === "pageLoad";
    return (
        <div
            className={`page-history-row page-history-row-${kind}`}
            css={css`
                display: flex;
                gap: 8px;
                padding: 1px 6px;
                white-space: pre-wrap;
                word-break: break-all;
                background: ${colors?.background ?? "transparent"};
                color: ${colors?.text ?? (isDetail ? "#4b5563" : "black")};
                font-weight: ${isStrong ? "bold" : "normal"};
                ${kind === "pageLoad" ? "border-top: 2px solid #6b7280;" : ""}
            `}
        >
            <span
                css={css`
                    flex: none;
                    width: 64px;
                    text-align: right;
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
