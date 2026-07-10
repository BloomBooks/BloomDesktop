import { post, useWatchApiObject } from "../utils/bloomApi";

// Shared, app-wide state for whether the user is signed in to BloomLibrary.org.
// This is used both by the account control in the workspace top bar and by the
// sign-in/out UI on the Publish screen, so that they always agree with each other.
// The backend keeps them in sync by broadcasting a "loginStateChanged" event on the
// "account" websocket context whenever the signed-in user changes (including changes
// made from another one of these UI locations, or from an external browser login).
export const useLoginState = () => {
    const status = useWatchApiObject<{ email: string }>(
        "account/status",
        { email: "" },
        "account",
        "loginStateChanged",
    );
    return {
        email: status.email || undefined,
        signIn: () => post("account/login"),
        signOut: () => post("account/logout"),
    };
};
