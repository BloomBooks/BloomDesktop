import { postJson, useWatchApiData } from "../utils/bloomApi";

// The TS end of the C# SharingApi (src/BloomExe/web/controllers/SharingApi.cs), which serves
// the Share dialog. Field names match the C# classes in src/BloomExe/Sharing.

export type SharingRole = "admin" | "editor";

// One person with access to the collection.
export interface ISharingMember {
    email: string;
    // Unknown until the person has used the collection; display falls back to the email.
    name?: string;
    role: SharingRole;
    // ISO dates, in UTC. invitedAt is when an admin gave them access.
    invitedAt: string;
    invitedBy: string;
    // The last time we know they used the collection: when they last opened it, or, for
    // someone given access because an old Team Collection's history shows them working in it,
    // their last action there. Missing if we have no record of their using it.
    lastSeen?: string;
}

export interface ISharingState {
    collectionName: string;
    // The Bloom Library sign-in; empty if nobody is signed in.
    signedInEmail: string;
    // The name the user registered with Bloom.
    signedInName: string;
    // False until someone first invites people, or, for a Team Collection, until one of its
    // administrators first opens the Share dialog.
    isShared: boolean;
    // Whether the signed-in person may invite people and change roles.
    canManage: boolean;
    members: ISharingMember[];
}

export interface IInvitation {
    email: string;
    role: SharingRole;
}

// The current sharing state, kept up to date whenever anything changes it. Undefined until the
// first answer arrives.
export function useSharingState(): ISharingState | undefined {
    return useWatchApiData<ISharingState | undefined>(
        "sharing/state",
        undefined,
        "sharing",
        "stateChanged",
    );
}

// Invite people. If the collection is not shared yet, this shares it, with the signed-in user
// as its admin. Resolves to whether it worked: postJson reports a failure to the user itself
// and then resolves with no response rather than rejecting.
export function invite(invitations: IInvitation[]): Promise<boolean> {
    return postJson("sharing/invite", { invitations }).then(
        (response) => !!response,
    );
}

export function setRole(email: string, role: SharingRole) {
    return postJson("sharing/setRole", { email, role });
}

export function removeMember(email: string) {
    return postJson("sharing/remove", { email });
}

export function sameEmail(a: string, b: string): boolean {
    return a.trim().toLowerCase() === b.trim().toLowerCase();
}
