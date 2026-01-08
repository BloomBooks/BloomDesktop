import { useEffect, useState } from "react";
import { ISelectedBookInfo } from "../app/selectedBook";
import { get } from "../utils/bloomApi";
import WebSocketManager from "../utils/WebSocketManager";

// This class supports the useIsSelected function (below), which is useful when many components
// (e.g., BookButtons) want to re-render when they change between being the selected book and
// not being the selected book, but we don't want them all to re-render when the selected book
// changes (since most of them remain unselected and there is no significant change). The idea is
// that at a high level one instance of BookSelectionManager is created, and passed down (possibly
// through a context) to the things that need to know whether a particular ID is selected.
// The one BookSelectionManager monitors the websocket message that tells us the selected book
// has changed.
// Through useIsSelected the leaf objects then have a boolean state which changes (causing a render)
// only when that particular book switches between being selected and not being selected.
export class BookSelectionManager {
    private selectedBookInfo: ISelectedBookInfo;

    public initialize = () => {
        get("app/selectedBookInfo", (response) => {
            this.setSelectedBookInfo(response.data);
        });
        WebSocketManager.addListener("book-selection", (e) => {
            if (e.id != "changed") {
                return;
            }
            this.setSelectedBookInfo(
                JSON.parse(e.message!) as ISelectedBookInfo,
            );
        });
    };

    private renameCallback: undefined | (() => void);

    // The selected button calls this to identify itself as the one that should be kicked into
    // the renaming state when F2 is pressed.
    public setRenameCallback(callback: undefined | (() => void)) {
        this.renameCallback = callback;
    }

    // Called when F2 is pressed, passes the request on to the selected button.
    public setRenaming(): void {
        if (this.renameCallback) {
            this.renameCallback();
        }
    }

    // A map where the key is a bookId and the value is an array of callbacks that should
    // be invokved when that ID becomes or ceases to be selected.
    private registrations = {};

    // Request a callback if the specified book ID becomes or ceases to be the selected book.
    // public so that useIsSelected can call it, but use with care...it can be tricky to
    // get unregistered properly. The intent is that all clients should use useIsSelected().
    // We handle the possibility that more than one client is interested in a callback about
    // a particular book being selected. This is useful both in case more than one kind of
    // client is interested, and also because there are cases, particularly in different
    // collections, where two or more books have the same ID.
    public registerForSelectedBookChanged(
        bookId: string,
        callback: (selected: boolean) => void,
    ) {
        let callbacks = this.registrations[bookId] as Array<
            (x: boolean) => void
        >;
        if (!callbacks) {
            callbacks = [];
            this.registrations[bookId] = callbacks;
        }
        callbacks.push(callback);
        if (callbacks.length > 10) {
            alert("cleanup is not working");
        }
    }

    public unregisterForSelectedBookChanged(
        bookId: string,
        callback: (selected: boolean) => void,
    ) {
        const callbacks = this.registrations[bookId] as Array<
            (x: boolean) => void
        >;
        if (!callbacks) {
            return;
        }
        const index = callbacks.indexOf(callback);
        if (index >= 0) {
            callbacks.splice(index, 1);
        }
        if (callbacks.length == 0) {
            delete this.registrations[bookId];
        }
    }

    // Helper method for setSelectedBookInfo. Informs all interested parties the the book with
    // the specified ID is becoming (or ceasing to be) selected.
    notify(id: string | undefined, becomingSelected: boolean) {
        if (!id) {
            return;
        }
        const callbacks = this.registrations[id] as Array<(x: boolean) => void>;
        if (!callbacks) {
            return;
        }
        callbacks.forEach((c) => c(becomingSelected));
    }

    setSelectedBookInfo(info: ISelectedBookInfo) {
        const oldBookInfo = this.selectedBookInfo;
        this.selectedBookInfo = info;
        if (oldBookInfo?.id !== info?.id) {
            this.notify(info?.id, true);
            this.notify(oldBookInfo?.id, false);
        }
    }

    // You should generally avoid using this in render methods, since nothing will
    // cause a re-render when it changes. It is intended for use in click handlers
    // and similar methods.
    public getSelectedBookInfo(): ISelectedBookInfo | undefined {
        return this.selectedBookInfo;
    }
}

// returns a boolean indicating whether the indicated bookId is currently the selected one.
// A re-render will be triggered when a change of selected book causes the answer to change.
// Intentionally a render will NOT be caused when the selection changes but this bookId
// remains unselected.
export function useIsSelected(
    manager: BookSelectionManager,
    bookId: string,
): boolean {
    const [selected, setSelected] = useState(
        bookId === manager.getSelectedBookInfo()?.id,
    );

    // There's some tricky stuff going on here. Our current expectation is that
    // manager and bookId will never change for a given client, so the effect only happens once,
    // and cleanup only happens when the component is discarded. I tried it with the effect
    // running every time, and when there were multiple clients for the same bookId,
    // only the first got notified. This is probably something to do with the first
    // callback causing a render which unregisters the others. Or it might be
    // that each call to this function is creating a distinct instance of the setSelected
    // function, and this messes things up somehow? Anyway, test carefully if something
    // violates the expectation that each client will be using a fixed manager and bookId.
    useEffect(() => {
        manager.registerForSelectedBookChanged(bookId, setSelected);
        return () =>
            manager.unregisterForSelectedBookChanged(bookId, setSelected);
    }, [manager, bookId]);
    return selected;
}
