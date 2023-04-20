import { useEffect, useState } from "react";
import { get } from "../utils/bloomApi";
import { useSubscribeToWebSocketForObjectInMessageParam } from "../utils/WebSocketManager";

export interface ISelectedBookInfo {
    id: string | undefined;
    saveable: boolean; // changes can safely be saved, including considering whether checked out if necessary
    collectionKind: "main" | "factory" | "error" | "other"; //error indicates the book is not usable for anything.
    aboutBookInfoUrl: string | undefined;
    isTemplate: boolean;
}

export const DefaultSelectedBookInfo: ISelectedBookInfo = {
    id: undefined,
    saveable: false,
    collectionKind: "error", // better to see no button at all until we know which it is
    aboutBookInfoUrl: undefined,
    isTemplate: false
};

// Anything that uses this will always have the current book info. The first render will see the default
// (nothing selected); immediately after, when we get the info from our server, another render will
// see the then-current information, and any time the selection changes another render will
// yield the new information, based on this code monitoring a websocket.
export function useMonitorBookSelection(): ISelectedBookInfo {
    const [selectedBookInfo, setSelectedBookInfo] = useState<ISelectedBookInfo>(
        DefaultSelectedBookInfo
    );

    // At this point, even a change of TC status on a book will cause Bloom to fire
    // BookSelection.InvokeSelectionChanged(), via TeamCollectionApi.UpdateUiForBook().
    // As a result, the top-level (WorkspaceView) handler for changing selection fires the websocket
    // event referenced here.
    useSubscribeToWebSocketForObjectInMessageParam<ISelectedBookInfo>(
        "book-selection",
        "changed",
        e => setSelectedBookInfo(e)
    );
    useEffect(() => {
        get("app/selectedBookInfo", response =>
            setSelectedBookInfo(response.data)
        );
    }, []);
    return selectedBookInfo;
}
