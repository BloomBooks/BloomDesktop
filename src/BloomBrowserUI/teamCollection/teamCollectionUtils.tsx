import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

export interface IBookTeamCollectionStatus {
    who: string;
    whoFirstName: string;
    whoSurname: string;
    when: string;
    where: string;
    currentUser: string;
    currentUserName: string;
    currentMachine: string;
    hasAProblem: boolean;
    hasInvalidRepoData: string; // error message, or empty if repo data is valid
    clickHereArg: string; // argument (currently, repo file name) needed to construct "Click here for help" message for corrupt zip
    changedRemotely: boolean;
    disconnected: boolean;
    newLocalBook: boolean;
    error: string;
    checkinMessage: string;
}

export const initialBookStatus: IBookTeamCollectionStatus = {
    who: "",
    whoFirstName: "",
    whoSurname: "",
    when: "",
    where: "",
    currentUser: "",
    currentUserName: "",
    currentMachine: "",
    hasAProblem: false,
    hasInvalidRepoData: "",
    clickHereArg: "",
    changedRemotely: false,
    disconnected: false,
    newLocalBook: false,
    error: "",
    checkinMessage: ""
};

export function useBookStatus(folderName: string): IBookTeamCollectionStatus {
    const [bookStatus, setBookStatus] = useState(initialBookStatus);
    const [reload, setReload] = useState(0);
    // Force a reload when told some book's status changed
    useSubscribeToWebSocketForEvent("bookStatus", "reload", () =>
        setReload(old => old + 1)
    );
    React.useEffect(() => {
        BloomApi.get(
            `teamCollection/bookStatus?folderName=${folderName}`,
            data => {
                setBookStatus(data.data as IBookTeamCollectionStatus);
            },
            err => {
                // Something went wrong. Maybe not registered. Already reported to Sentry, we don't need
                // another 'throw' here, with less information. Displaying the message may tell the user
                // something. I don't think it's worth localizing the fallback message here, which is even
                // less likely to be seen.
                // Enhance: we could display a message telling them to register and perhaps a link to the
                // registration dialog.
                const errorMessage =
                    err?.response?.statusText ??
                    "Bloom could not determine the status of this book";
                setBookStatus({ ...bookStatus, error: errorMessage });
            }
        );
    }, [reload]);
    return bookStatus;
}
