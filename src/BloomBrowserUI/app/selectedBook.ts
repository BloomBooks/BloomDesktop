import { useEffect, useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { useSubscribeToWebSocketForObject } from "../utils/WebSocketManager";

export interface ISelectedBookInfo {
    id: string | undefined;
    editable: boolean; // truly editable, including considering whether checked out if necessary
    canMakeBook: boolean; // true if we can make a book from this source. Should never be true if editable is.
}
export function useSelectedBookInfo(): ISelectedBookInfo {
    const [selectedBookInfo, setSelectedBookInfo] = useState<ISelectedBookInfo>(
        {
            id: undefined,
            editable: false,
            canMakeBook: false
        }
    );

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
