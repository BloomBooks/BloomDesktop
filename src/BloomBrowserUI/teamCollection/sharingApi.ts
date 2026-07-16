import * as React from "react";
import { useState } from "react";
import {
    get,
    getApiDataOnce,
    post,
    postJson,
    resetApiDataOnceCacheForTests,
    useWatchApiData,
    useWatchApiDataWithReload,
} from "../utils/bloomApi";

// The TS end of interactions with the `SharingApi` C# class (task 06). Names here are kept in
// sync with Design/CloudTeamCollections/CONTRACTS.md.

// Matches CONTRACTS.md: in dev-auth mode, sign-in is a plain email/password form; in "cloud"
// mode (Option A, decided 8 Jul 2026) it is the BloomLibrary browser-based flow -- see
// openBrowserSignIn() below and CONTRACTS.md's "Auth (Option A)" section.
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
    // The durable, editable display name (tc.members.display_name, 20260713000001); undefined
    // until someone sets it via setDisplayName below. Display falls back to the email.
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
    return useWatchApiData(
        "sharing/loginState",
        initialLoginState,
        "sharing",
        "loginState",
    );
}

// Posts dev-auth-mode credentials. Resolves once the server has updated the login state;
// callers should watch useSharingLoginState() (or the "sharing"/"loginState" event) for the result.
export function signIn(email: string, password: string) {
    return postJson("sharing/login", { email, password });
}

export function signOut() {
    return post("sharing/logout");
}

// "cloud" mode's sign-in action: there is no password form, so this just tells Bloom to open
// the BloomLibrary-hosted login page in the user's browser (SharingApi.HandleOpenBrowserSignIn
// -> BloomLibraryAuthentication.LogIn). That page forwards the resulting tokens back to Bloom
// directly (CONTRACTS.md's "Auth (Option A)" token-receipt endpoint); the caller does not await
// a result here -- watch useSharingLoginState() (or the "sharing"/"loginState" event) instead,
// exactly as the "dev" mode signIn() callers already do.
export function openBrowserSignIn() {
    return post("sharing/openBrowserSignIn");
}

// Fetches the approved-accounts list for a cloud Team Collection, refetching whenever the server
// fires "sharing"/"membersChanged" (or the caller invokes reload). No fetch until collectionId is
// known.
export function useSharingMembers(collectionId: string): {
    members: IApprovedMember[];
    reload: () => void;
} {
    const { data, reload } = useWatchApiDataWithReload<IApprovedMember[]>(
        collectionId
            ? `sharing/members?collectionId=${encodeURIComponent(collectionId)}`
            : undefined,
        [],
        "sharing",
        "membersChanged",
    );
    return { members: data, reload };
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

// Sets a member's human-readable display name (shown with the email as fallback wherever the
// member appears -- member list, checkout status, history). An empty string clears it.
// Server-side, an admin may set anyone's name and a claimed member their own.
export function setDisplayName(
    collectionId: string,
    email: string,
    displayName: string,
) {
    return postJson("sharing/setDisplayName", {
        collectionId,
        email,
        displayName,
    });
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

// Result of a successful collections/pullDown: the local .bloomCollection file path the
// collection was pulled down to, so the caller can open it directly (see
// JoinCloudCollectionDialog's handleJoinClick) instead of leaving the user to find the new
// collection in the chooser themselves. A settings-file path, not a folder, because
// workspace/openCollection expects what the chooser's cards pass it.
export interface IPullDownResult {
    collectionPath: string;
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
function getEnabledExperimentalFeaturesOnce(): Promise<string> {
    return getApiDataOnce(
        "app/enabledExperimentalFeatures",
        (data) => (data as string) ?? "",
    );
}

// Test-only: forget the cached experimental-features fetch so each test's endpoint mocks
// are observed. Call from beforeEach; production code must never call this.
export function resetSharingApiCachesForTests() {
    resetApiDataOnceCacheForTests();
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
