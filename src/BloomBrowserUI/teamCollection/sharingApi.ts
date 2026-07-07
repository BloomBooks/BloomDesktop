import * as React from "react";
import { useState } from "react";
import { get, post, postJson } from "../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

// The TS end of interactions with the (not-yet-implemented, see task 06) `SharingApi` C# class.
// Wave-1 (this task) only needs shells that call these endpoints; the endpoints themselves,
// and the events they raise, land in task 06. Names here are kept in sync with
// Design/CloudTeamCollections/CONTRACTS.md so that wiring up the real backend is a drop-in.

// Matches CONTRACTS.md: in dev-auth mode, sign-in is a plain email/password form; in the
// eventual production ("cloud") mode, it will be the BloomLibrary browser-based flow.
export type SharingLoginMode = "dev" | "cloud";

export interface ISharingLoginState {
    mode: SharingLoginMode;
    signedIn: boolean;
    email?: string;
    emailVerified?: boolean;
}

export const initialLoginState: ISharingLoginState = {
    mode: "dev",
    signedIn: false,
};

export type SharingRole = "admin" | "member";

// One row of the approved-accounts list shown in SharingPanel.
export interface IApprovedMember {
    email: string;
    // Only known once the invitation has been claimed (the person has signed in at least once).
    name?: string;
    role: SharingRole;
    // True once someone has signed in with this email and been linked to the approval.
    claimed: boolean;
}

export interface ICloudCollectionSummary {
    collectionId: string;
    name: string;
    role: SharingRole;
}

// Fetches and keeps in sync the current sign-in state (`sharing/loginState`).
// Re-queries whenever the "sharing"/"loginState" websocket event fires (e.g. after login/logout
// completes in another part of the UI).
export function useSharingLoginState(): ISharingLoginState {
    const [loginState, setLoginState] = useState(initialLoginState);
    const [reload, setReload] = useState(0);
    useSubscribeToWebSocketForEvent("sharing", "loginState", () =>
        setReload((old) => old + 1),
    );
    React.useEffect(() => {
        get("sharing/loginState", (result) => {
            setLoginState(result.data as ISharingLoginState);
        });
    }, [reload]);
    return loginState;
}

// Posts dev-auth-mode credentials. Resolves once the server has updated the login state;
// callers should watch useSharingLoginState() (or the "sharing"/"loginState" event) for the result.
export function signIn(email: string, password: string) {
    return postJson("sharing/login", { email, password });
}

export function signOut() {
    return post("sharing/logout");
}

// Fetches the approved-accounts list for a cloud Team Collection.
export function useSharingMembers(collectionId: string): {
    members: IApprovedMember[];
    reload: () => void;
} {
    const [members, setMembers] = useState<IApprovedMember[]>([]);
    const [generation, setGeneration] = useState(0);
    useSubscribeToWebSocketForEvent("sharing", "membersChanged", () =>
        setGeneration((old) => old + 1),
    );
    React.useEffect(() => {
        if (!collectionId) return;
        get(
            `sharing/members?collectionId=${encodeURIComponent(collectionId)}`,
            (result) => {
                setMembers((result.data as IApprovedMember[]) ?? []);
            },
        );
    }, [collectionId, generation]);
    return { members, reload: () => setGeneration((old) => old + 1) };
}

export function addApproval(
    collectionId: string,
    email: string,
    role: SharingRole,
) {
    return postJson("sharing/addApproval", { collectionId, email, role });
}

// Removing an approval force-unlocks any books that user currently has checked out.
export function removeApproval(collectionId: string, email: string) {
    return postJson("sharing/removeApproval", { collectionId, email });
}

export function setRole(
    collectionId: string,
    email: string,
    role: SharingRole,
) {
    return postJson("sharing/setRole", { collectionId, email, role });
}

// The "Get my Team Collections" list on the collection chooser.
export function useMyCloudCollections(shouldQuery: boolean): {
    collections: ICloudCollectionSummary[];
    loading: boolean;
} {
    const [collections, setCollections] = useState<ICloudCollectionSummary[]>(
        [],
    );
    const [loading, setLoading] = useState(false);
    React.useEffect(() => {
        if (!shouldQuery) return;
        setLoading(true);
        get("collections/mine", (result) => {
            setCollections((result.data as ICloudCollectionSummary[]) ?? []);
            setLoading(false);
        });
    }, [shouldQuery]);
    return { collections, loading };
}

export function pullDownCollection(collectionId: string) {
    return postJson("collections/pullDown", { collectionId });
}

// Token used in Settings > Advanced Settings > Experimental Features to gate the cloud-sharing
// UI; must match ExperimentalFeatures.kCloudTeamCollections in the C# code.
const kCloudTeamCollectionsExperimentalFeatureToken = "cloud-team-collections";

// The enabled-experimental-features list is fetched at most ONCE per page load and shared by
// every caller of the hook below. This matters because the hook is used by per-book components
// (BookButton renders once per book, and remounts on every switch to the Collection tab) — an
// uncached per-mount request meant hundreds of identical HTTP calls. Changing experimental
// features requires reopening dialogs/pages anyway, so page-load granularity loses nothing.
let enabledExperimentalFeaturesPromise: Promise<string> | undefined;

function getEnabledExperimentalFeaturesOnce(): Promise<string> {
    if (!enabledExperimentalFeaturesPromise) {
        enabledExperimentalFeaturesPromise = new Promise((resolve) =>
            get("app/enabledExperimentalFeatures", (result) =>
                resolve((result.data as string) ?? ""),
            ),
        );
    }
    return enabledExperimentalFeaturesPromise;
}

// Test-only: forget the cached experimental-features fetch so each test's endpoint mocks
// are observed. Call from beforeEach; production code must never call this.
export function resetSharingApiCachesForTests() {
    enabledExperimentalFeaturesPromise = undefined;
}

// Whether the user has turned on the "cloud-team-collections" experimental feature. Backed by
// the same `app/enabledExperimentalFeatures` endpoint the Talking Book toolbox already uses
// (a comma-separated list of enabled tokens), so no new C# is required for this Wave-1 gate.
export function useIsCloudTeamCollectionsExperimentalFeatureEnabled(): boolean {
    const [enabled, setEnabled] = useState(false);
    React.useEffect(() => {
        let cancelled = false;
        getEnabledExperimentalFeaturesOnce().then((tokens) => {
            if (!cancelled)
                setEnabled(
                    tokens.includes(
                        kCloudTeamCollectionsExperimentalFeatureToken,
                    ),
                );
        });
        return () => {
            cancelled = true;
        };
    }, []);
    return enabled;
}

// Kicks off the (Wave-3) cloud Team Collection creation flow: uploads the current local
// collection as the initial version of a new cloud-backed Team Collection.
// Uses postJson (rather than post, which is fire-and-forget and does not return a promise)
// so callers can await/`.then()` completion to drive the initial-Send progress UI.
export function createCloudTeamCollection() {
    return postJson("teamCollection/createCloudTeamCollection", {});
}
