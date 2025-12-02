import * as React from "react";
import { useState } from "react";
import { get, getBoolean } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

// The TS end of various interactions with the TeamCollectionApi class in C#

// Defines the data expected from a query to `teamCollection/bookStatus?folderName=${folderName}`
// Keep in sync with the value returned by TeamCollectionApi.GetBookStatusJson.
export interface IBookTeamCollectionStatus {
    who: string;
    whoFirstName: string;
    whoSurname: string;
    when: string;
    where: string;
    currentUser: string;
    currentUserName: string;
    currentMachine: string;
    hasConflictingChange: boolean; // and thus the book will move to Lost & Found
    invalidRepoDataErrorMsg: string; // error message, or empty if repo data is valid
    clickHereArg: string; // argument (currently, repo file name) needed to construct "Click here for help" message for corrupt zip
    isChangedRemotely: boolean; // and thus needs to be reloaded
    isDisconnected: boolean;
    isNewLocalBook: boolean;
    error: string; // This one is not current sent from the C# side.
    checkinMessage: string;
    isUserAdmin: boolean;
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
    hasConflictingChange: false,
    invalidRepoDataErrorMsg: "",
    clickHereArg: "",
    isChangedRemotely: false,
    isDisconnected: false,
    isNewLocalBook: false,
    error: "",
    checkinMessage: "",
    isUserAdmin: false
};

export function useTColBookStatus(
    folderName: string,
    inEditableCollection: boolean
): IBookTeamCollectionStatus {
    const [bookStatus, setBookStatus] = useState(initialBookStatus);
    const [reload, setReload] = useState(0);
    // Force a reload when told some book's status changed
    useSubscribeToWebSocketForEvent("bookTeamCollectionStatus", "reload", () =>
        setReload(old => old + 1)
    );
    React.useEffect(() => {
        // if it's not in the editable collection, economize and don't call; the initialBookStatus will do.
        if (inEditableCollection) {
            const params = new URLSearchParams();
            params.set("folderName", folderName);
            get(
                `teamCollection/bookStatus?${params.toString()}`,
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
                    setBookStatus({
                        ...bookStatus,
                        error: errorMessage
                    });
                }
            );
        }
    }, [reload]);
    return bookStatus;
}

export function useIsTeamCollection() {
    const [isTeamCollection, setIsTeamCollection] = React.useState(false);
    React.useEffect(() => {
        getBoolean("teamCollection/isTeamCollectionEnabled", teamCollection =>
            setIsTeamCollection(teamCollection)
        );
    });
    return isTeamCollection;
}
