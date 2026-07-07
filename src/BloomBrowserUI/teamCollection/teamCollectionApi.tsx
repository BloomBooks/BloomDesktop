import * as React from "react";
import { useState } from "react";
import { get, getBoolean } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { useIsCloudTeamCollectionsExperimentalFeatureEnabled } from "./sharingApi";

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
    checkInMessage: string;
    isUserAdmin: boolean;
    // --- Cloud Team Collections additions (CONTRACTS.md "Book-status JSON", additive) ---
    // Folder Team Collections never populate these fields, so `undefined` must always be
    // treated as "behave exactly as before" everywhere they're read.
    localVersionSeq?: number; // sequence number of the version currently on disk
    repoVersionSeq?: number; // sequence number of the latest version in the repo (may be newer)
    signedIn?: boolean; // whether the current user is signed in to the cloud account
    requiresSignIn?: boolean; // true for cloud-backed collections, which need an authenticated user to check out/in
    offlineDisabledReason?: string; // non-empty => this book can't be used at all while offline (e.g. it has never been downloaded to this computer)
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
    checkInMessage: "",
    isUserAdmin: false,
};

export function useTColBookStatus(
    folderName: string,
    inEditableCollection: boolean,
): IBookTeamCollectionStatus {
    const [bookStatus, setBookStatus] = useState(initialBookStatus);
    const [reload, setReload] = useState(0);
    // Force a reload when told some book's status changed
    useSubscribeToWebSocketForEvent("bookTeamCollectionStatus", "reload", () =>
        setReload((old) => old + 1),
    );
    React.useEffect(() => {
        // if it's not in the editable collection, economize and don't call; the initialBookStatus will do.
        if (inEditableCollection) {
            const params = new URLSearchParams();
            params.set("folderName", folderName);
            get(
                `teamCollection/bookStatus?${params.toString()}`,
                (data) => {
                    setBookStatus(data.data as IBookTeamCollectionStatus);
                },
                (err) => {
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
                        error: errorMessage,
                    });
                },
            );
        }
    }, [reload]);
    return bookStatus;
}

export function useIsTeamCollection() {
    const [isTeamCollection, setIsTeamCollection] = React.useState(false);
    React.useEffect(() => {
        getBoolean("teamCollection/isTeamCollectionEnabled", (teamCollection) =>
            setIsTeamCollection(teamCollection),
        );
    }, []);
    return isTeamCollection;
}

// --- Cloud Team Collections: Wave-2 shells against mocked endpoints (see task 08) ---
// The endpoints referenced below (teamCollection/capabilities, teamCollection/tcStatusMetadata,
// teamCollection/cloudCollectionId, teamCollection/isUserAdmin) do not exist in the C# code yet;
// they land with task 06/Wave-3 wiring. Every hook here only calls its endpoint when the
// cloud-team-collections experimental feature is on, so folder Team Collections (the overwhelming
// majority of current usage) never make the extra request and never see any UI difference.

// Backend capability flags (CONTRACTS.md, additive to the book-status JSON): tell the UI what the
// current Team Collection's backend can do, so components branch on capability rather than on
// concrete backend type (folder vs cloud). All default to false, which is exactly today's
// folder-TC behavior.
export interface ITeamCollectionCapabilities {
    supportsVersionHistory: boolean;
    supportsSharingUi: boolean;
    requiresSignIn: boolean;
}

export const initialTeamCollectionCapabilities: ITeamCollectionCapabilities = {
    supportsVersionHistory: false,
    supportsSharingUi: false,
    requiresSignIn: false,
};

export function useTeamCollectionCapabilities(): ITeamCollectionCapabilities {
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const [capabilities, setCapabilities] = useState(
        initialTeamCollectionCapabilities,
    );
    React.useEffect(() => {
        if (!cloudFeatureEnabled) return;
        get("teamCollection/capabilities", (result) => {
            setCapabilities(
                (result.data as ITeamCollectionCapabilities) ??
                    initialTeamCollectionCapabilities,
            );
        });
    }, [cloudFeatureEnabled]);
    return capabilities;
}

// True if any capability flag indicates a cloud (S3 + Supabase) backend rather than a folder one.
// Prefer this over checking a single flag, so callers that just want "is this a cloud TC" stay
// correct even if a future capability is added or one of the mocked defaults changes.
export function isCloudTeamCollection(
    capabilities: ITeamCollectionCapabilities,
): boolean {
    return (
        capabilities.supportsVersionHistory ||
        capabilities.supportsSharingUi ||
        capabilities.requiresSignIn
    );
}

// Live metadata behind the status button/chip (e.g. "Updates Available (3 books)"). Kept separate
// from the plain `teamCollection/tcStatus` enum endpoint (owned by other tasks) so this task can
// add to it without touching that contract.
export interface ITeamCollectionStatusMetadata {
    updatesAvailableCount?: number;
}

export function useTeamCollectionStatusMetadata(): ITeamCollectionStatusMetadata {
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const [metadata, setMetadata] = useState<ITeamCollectionStatusMetadata>({});
    const [reload, setReload] = useState(0);
    useSubscribeToWebSocketForEvent(
        "teamCollection",
        "statusMetadataChanged",
        () => setReload((old) => old + 1),
    );
    React.useEffect(() => {
        if (!cloudFeatureEnabled) return;
        get("teamCollection/tcStatusMetadata", (result) => {
            setMetadata((result.data as ITeamCollectionStatusMetadata) ?? {});
        });
    }, [cloudFeatureEnabled, reload]);
    return metadata;
}

// The cloud collection id (server `collections.id`) of the currently open collection, needed to
// call SharingApi endpoints. Empty string for a folder Team Collection (or no collection open).
export function useCloudCollectionId(): string {
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const [collectionId, setCollectionId] = useState("");
    React.useEffect(() => {
        if (!cloudFeatureEnabled) return;
        get("teamCollection/cloudCollectionId", (result) => {
            setCollectionId((result.data as string) ?? "");
        });
    }, [cloudFeatureEnabled]);
    return collectionId;
}

// Whether the current user is an administrator of the currently open Team Collection. Used by the
// collection-tab Share button to decide whether SharingPanel opens in manage or read-only mode.
export function useIsTeamCollectionAdmin(): boolean {
    const cloudFeatureEnabled =
        useIsCloudTeamCollectionsExperimentalFeatureEnabled();
    const [isAdmin, setIsAdmin] = useState(false);
    React.useEffect(() => {
        if (!cloudFeatureEnabled) return;
        getBoolean("teamCollection/isUserAdmin", setIsAdmin);
    }, [cloudFeatureEnabled]);
    return isAdmin;
}
