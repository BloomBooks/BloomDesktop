import { useState } from "react";
import { useSubscribeToWebSocketForObject } from "../utils/WebSocketManager";

export interface ISelectedBookInfo {
    id: string | undefined;
    editable: boolean;
}
export function useSelectedBookInfo(): ISelectedBookInfo {
    const [selectedBookInfo, setSelectedBookInfo] = useState<ISelectedBookInfo>(
        {
            id: undefined,
            editable: false
        }
    );

    useSubscribeToWebSocketForObject<ISelectedBookInfo>(
        "book-selection",
        "changed",
        e => setSelectedBookInfo(e)
    );
    return selectedBookInfo;
}
