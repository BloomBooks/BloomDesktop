/** @jsxImportSource @emotion/react */
import { css } from "@emotion/react";
import * as React from "react";
import { useLayoutEffect, useRef, useState } from "react";
import { Rnd } from "react-rnd";
import { bloom } from "./bloomTheme";

// A floating window with a drag handle title bar and resize handles, built on react-rnd.
// The title bar (class "gte-drag-handle") is the only drag region so that controls inside
// the body remain clickable.
//
// The panel is mounted into the editable-page document, whose viewport can be smaller than
// our desired size. So before rendering the Rnd we measure that document's window and clamp
// the initial size/position to fit; otherwise an oversized panel pins horizontal dragging
// (the element is wider than the bounds) and pushes the resize handles off-screen. We bound
// the panel to its parent (a full-viewport overlay supplied by the host) so it can be moved
// on both axes and resized, and can never be dragged completely out of reach.
type Box = { x: number; y: number; width: number; height: number };

export const DraggableResizableFrame: React.FunctionComponent<{
    title: string;
    onClose: () => void;
    defaultWidth?: number;
    defaultHeight?: number;
    minWidth?: number;
    minHeight?: number;
    // When set, the frame's last size and position are remembered (in the page's localStorage)
    // under this key and restored the next time a frame with the same key opens.
    storageKey?: string;
    children?: React.ReactNode;
}> = (props) => {
    const probeRef = useRef<HTMLDivElement>(null);
    const rndRef = useRef<Rnd>(null);
    const [box, setBox] = useState<Box | null>(null);

    // Where we remember the size/position. Scoped to the storageKey the host passed.
    const storageKey = props.storageKey
        ? `gte-frame-geometry:${props.storageKey}`
        : null;

    // Read a previously saved box, ignoring anything corrupt or not in the expected shape.
    const readSavedBox = (win: Window | null | undefined): Box | null => {
        if (!storageKey || !win) return null;
        try {
            const raw = win.localStorage.getItem(storageKey);
            if (!raw) return null;
            const v = JSON.parse(raw);
            if (
                typeof v?.x === "number" &&
                typeof v?.y === "number" &&
                typeof v?.width === "number" &&
                typeof v?.height === "number"
            )
                return v;
        } catch {
            // Corrupt or unreadable storage: fall back to the default geometry.
        }
        return null;
    };

    // Remember the current size/position so it can be restored next time.
    const saveBox = (node: HTMLElement, x: number, y: number) => {
        if (!storageKey) return;
        const win = node.ownerDocument.defaultView;
        if (!win) return;
        const saved: Box = {
            x,
            y,
            width: node.offsetWidth,
            height: node.offsetHeight,
        };
        win.localStorage.setItem(storageKey, JSON.stringify(saved));
    };

    useLayoutEffect(() => {
        // probeRef lives in the page document, so its window is the page iframe's window.
        const win = probeRef.current?.ownerDocument?.defaultView;
        const vw = win?.innerWidth ?? 800;
        const vh = win?.innerHeight ?? 600;
        // Prefer a remembered size/position, clamped to the current viewport; otherwise use the
        // default size tucked into the top-left.
        const saved = readSavedBox(win);
        if (saved) {
            const width = Math.min(saved.width, vw - 24);
            const height = Math.min(saved.height, vh - 24);
            const x = Math.min(Math.max(0, saved.x), Math.max(0, vw - width));
            const y = Math.min(Math.max(0, saved.y), Math.max(0, vh - 40));
            setBox({ x, y, width, height });
            return;
        }
        const width = Math.min(props.defaultWidth ?? 700, vw - 24);
        const height = Math.min(props.defaultHeight ?? 540, vh - 24);
        const x = Math.max(8, Math.min(48, vw - width - 8));
        const y = Math.max(8, Math.min(48, vh - height - 8));
        setBox({ x, y, width, height });
        // storageKey is derived from a prop and stable for the life of the frame.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.defaultWidth, props.defaultHeight]);

    if (!box) {
        // Transient: a zero-impact node we use only to find the page document/window.
        return <div ref={probeRef} />;
    }

    // The host mounts us inside a full-window overlay that is pointer-events:none so the page
    // stays clickable. But Bloom's edit surface is built from iframes, and the moment a drag
    // crosses an iframe the top document stops receiving mouse events and react-rnd freezes.
    // So while dragging/resizing we turn the overlay into a pointer-events shield (it sits above
    // the iframes at the same z-index), then turn it back off when the gesture ends.
    const setShield = (node: HTMLElement | null | undefined, on: boolean) => {
        const overlay = node?.parentElement;
        if (overlay) overlay.style.pointerEvents = on ? "auto" : "none";
    };

    // react-rnd's built-in `bounds` clamps incorrectly in this WebView2 + fractional-DPI
    // environment (it pins the panel to an edge). So we don't use `bounds`; instead, after a
    // drag we clamp the position ourselves so the whole panel stays horizontally on-screen and
    // the title bar stays reachable vertically.
    const clampIntoView = (node: HTMLElement, x: number, y: number) => {
        const win = node.ownerDocument.defaultView;
        if (!win) return;
        const maxX = Math.max(0, win.innerWidth - node.offsetWidth);
        const maxY = Math.max(0, win.innerHeight - 40); // keep the title bar reachable
        const cx = Math.min(Math.max(0, x), maxX);
        const cy = Math.min(Math.max(0, y), maxY);
        if (cx !== x || cy !== y)
            rndRef.current?.updatePosition({ x: cx, y: cy });
    };

    return (
        <Rnd
            ref={rndRef}
            default={box}
            minWidth={props.minWidth ?? 360}
            minHeight={props.minHeight ?? 280}
            dragHandleClassName="gte-drag-handle"
            onDragStart={(_e, d) => setShield(d.node, true)}
            onDragStop={(_e, d) => {
                setShield(d.node, false);
                clampIntoView(d.node, d.x, d.y);
                saveBox(d.node, d.x, d.y);
            }}
            onResizeStart={(_e, _dir, ref) => setShield(ref, true)}
            onResizeStop={(_e, _dir, ref, _delta, position) => {
                setShield(ref, false);
                saveBox(ref, position.x, position.y);
            }}
            // re-resizable sets the root's inline display, which would beat an emotion class.
            // So the flex column that bounds the scrollable middle region must go in `style`,
            // where it wins. Visual styling stays in css below.
            style={{
                display: "flex",
                flexDirection: "column",
                overflow: "hidden",
            }}
            css={css`
                z-index: 6000;
                pointer-events: auto;
                background: white;
                border: 1px solid #b0b0b0;
                border-radius: 6px;
                box-shadow: 0 6px 24px rgba(0, 0, 0, 0.3);
                overflow: hidden;
                font-family: ${bloom.fontStack};
                color: #202020;
            `}
        >
            <div
                className="gte-drag-handle"
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    padding: 6px 10px;
                    background: ${bloom.blue};
                    color: white;
                    cursor: move;
                    user-select: none;
                    flex-shrink: 0;
                `}
            >
                <span
                    css={css`
                        font-size: 14px;
                        font-weight: 600;
                    `}
                >
                    {props.title}
                </span>
                <button
                    type="button"
                    onClick={props.onClose}
                    title="Close"
                    css={css`
                        border: none;
                        background: transparent;
                        color: white;
                        font-size: 16px;
                        line-height: 1;
                        cursor: pointer;
                        padding: 2px 6px;
                        &:hover {
                            color: #ffd0d0;
                        }
                    `}
                >
                    ✕
                </button>
            </div>
            <div
                css={css`
                    flex: 1;
                    min-height: 0;
                    display: flex;
                    flex-direction: column;
                `}
            >
                {props.children}
            </div>
        </Rnd>
    );
};
