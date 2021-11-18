import { useEffect, useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { useSubscribeToWebSocketForObject } from "../utils/WebSocketManager";

export interface ISelectedBookInfo {
    id: string | undefined;
    saveable: boolean; // changes can safely be saved, including considering whether checked out if necessary
    collectionKind: "main" | "factory" | "other"; // true if we can make a book from this source. Should never be true if saveable is.
}

// Anything that uses this will always have the current book info. The first render will see the default
// (nothing selected); immediately after, when we get the info from our server, another render will
// see the then-current information, and any time the selection changes another render will
// yield the new information, based on this code monitoring a websocket.
export function useMonitorBookSelection(): ISelectedBookInfo {
    const [selectedBookInfo, setSelectedBookInfo] = useState<ISelectedBookInfo>(
        {
            id: undefined,
            saveable: false,
            collectionKind: "main" // better to see the 'edit' button until we know which it is
        }
    );

    // At this point, even a change of TC status on a book will cause Bloom to fire
    // BookSelection.InvokeSelectionChanged(), via TeamCollectionApi.UpdateUiForBook().
    // As a result, the top-level (WorkspaceView) handler for changing selection fires the websocket
    // event referenced here.
    useSubscribeToWebSocketForObject<ISelectedBookInfo>(
        "book-selection",
        "changed",
        e => setSelectedBookInfo(e)
    );
    useEffect(() => {
        BloomApi.get("app/selectedBookInfo", response =>
            setSelectedBookInfo(response.data)
        );
    }, []);
    return selectedBookInfo;
}
