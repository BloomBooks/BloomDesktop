import { postJson, useWatchApiData } from "../utils/bloomApi";

// The TS end of the C# SharingApi (src/BloomExe/web/controllers/SharingApi.cs), which serves
// the Share dialog. Field names match the C# classes in src/BloomExe/Sharing.

export type SharingRole = "admin" | "editor";

export type SharingMemberStatus = "invited" | "active";

// One person with access to the collection.
export interface ISharingMember {
    email: string;
    // Unknown until the person has used the collection; display falls back to the email.
    name?: string;
    role: SharingRole;
    status: SharingMemberStatus;
    // ISO dates, in UTC.
    invitedAt: string;
    invitedBy: string;
    lastSeen?: string;
}

// Someone the old Team Collection's history shows has worked in this collection, whom an
// admin may want to invite.
export interface ISharingSuggestion {
    email: string;
    name?: string;
    role: SharingRole;
    lastActivity: string;
}

export interface ISharingState {
    collectionName: string;
    // The Bloom Library sign-in; empty if nobody is signed in.
    signedInEmail: string;
    // The name the user registered with Bloom.
    signedInName: string;
    // False until someone first invites people.
    isShared: boolean;
    // Whether the signed-in person may invite people and change roles.
    canManage: boolean;
    members: ISharingMember[];
    // Only filled in for someone who canManage.
    suggestions: ISharingSuggestion[];
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

// Stop suggesting these people (only possible once the collection is shared).
export function dismissSuggestions(emails: string[]) {
    return postJson("sharing/dismissSuggestions", { emails });
}

export function sameEmail(a: string, b: string): boolean {
    return a.trim().toLowerCase() === b.trim().toLowerCase();
}
