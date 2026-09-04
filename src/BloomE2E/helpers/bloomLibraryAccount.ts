// Sign a test's Bloom in to dev.bloomlibrary.org for real, as a dedicated test account.
//
// Why this is possible now: every Bloom a test launches keeps its user settings, the Bloom Library
// login included, in a folder of its own (bloomApp.userSettingsDir; see fixtures/launchBloom.ts).
// So a test can sign a real account in without touching the developer's own Bloom, and Bloom's
// bulk-upload child process, which reads the login back from that folder, uploads as the test
// account rather than as the developer. See AUTOMATION-DEBT.md, "The Bloom Library login cannot be
// done for real in a test".
//
// The account and its password: the email is a constant here, because it is not secret and a reader
// should see which account a test signs in as (an assertion can check it too — the Sign out button
// carries it). Only the password is kept out of the source, in the BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD
// environment variable (User scope on a developer's machine, a repository secret in CI). A run
// without it skips the tests that need it locally, and fails in CI, so a missing secret is noticed.
//
// What this file does NOT do: reach the server to read or delete books. That is bloomLibraryServer.ts,
// which owns the sandbox back ends. This file borrows that module's login type and its parse-server
// address, so there is one description of the sandbox, not two.

import { test, type Page } from "@playwright/test";
import { apiPost } from "./api";
import {
    DEV_PARSE_APPLICATION_ID,
    DEV_PARSE_SERVER_URL,
    type IBloomLibraryLogin,
} from "./bloomLibraryServer";

/**
 * The test account every real-login test signs in as. Not secret: it is here so a reader can see
 * which account a test uses, and an assertion can check it. Only the password is kept out of the
 * source (see PASSWORD_ENV_VAR). The account exists only for these tests, on dev.bloomlibrary.org.
 */
export const TEST_ACCOUNT_EMAIL = "e2e-tester@example.org";

/** The environment variable that carries the test account's dev.bloomlibrary.org password. */
export const PASSWORD_ENV_VAR = "BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD";

// dev.bloomlibrary.org's Firebase auth, which the website uses to sign a user in before exchanging
// the result for a parse-server session. The key ships in the website's JS bundle, so it is not a
// secret. The Firebase project is shared with production; it is the parse server (dev, from
// bloomLibraryServer.ts) that makes this a sandbox login.
const FIREBASE_API_KEY = "AIzaSyACJ7fi7_Rg_bFgTIacZef6OQckr6QKoTY";

/**
 * Whether the test account's password is available. When it is not, a real-login test cannot run.
 * See skipIfNoLibraryPassword, which is how a test acts on this.
 */
export function isLibraryPasswordConfigured(): boolean {
    return !!process.env[PASSWORD_ENV_VAR];
}

/**
 * Skip the current test when the test account's password is not set — but only off CI. On CI a
 * missing password is a broken secret, not a developer who has not set one up, so there the test is
 * left to fail rather than quietly skipped. Call at the top of a test that signs in for real.
 */
export function skipIfNoLibraryPassword(): void {
    if (isLibraryPasswordConfigured()) return;
    if (process.env.CI)
        throw new Error(
            `${PASSWORD_ENV_VAR} is not set, so this run cannot sign in to dev.bloomlibrary.org as ` +
                `${TEST_ACCOUNT_EMAIL}. On CI this is a missing repository secret; add it.`,
        );
    test.skip(
        true,
        `${PASSWORD_ENV_VAR} is not set. Set it (User environment scope) from the team password ` +
            `manager to run the tests that sign in to dev.bloomlibrary.org as ${TEST_ACCOUNT_EMAIL}.`,
    );
}

/**
 * Sign the test account in to dev.bloomlibrary.org and return the login, doing exactly what the
 * website's login-for-editor page does: authenticate to Firebase with the email and password, then
 * exchange the Firebase token for a parse-server session (the bloomLink cloud function links or
 * creates the parse user, then a users POST logs in). Throws with a diagnostic naming the step that
 * failed. The account's email must be verified, or the parse server refuses it.
 */
export async function getDevBloomLibraryLogin(): Promise<IBloomLibraryLogin> {
    const password = process.env[PASSWORD_ENV_VAR];
    if (!password)
        throw new Error(
            `${PASSWORD_ENV_VAR} is not set; call skipIfNoLibraryPassword first.`,
        );

    const firebase = await postJson(
        `https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${FIREBASE_API_KEY}`,
        { email: TEST_ACCOUNT_EMAIL, password, returnSecureToken: true },
        "Firebase sign-in",
    );

    // bloomLink links the Firebase user to a parse user (creating one if needed) before the users
    // POST logs in; the website calls it first for the same reason.
    await postJson(
        `${DEV_PARSE_SERVER_URL}/functions/bloomLink`,
        { token: firebase.idToken, id: TEST_ACCOUNT_EMAIL },
        "parse-server bloomLink",
        { "X-Parse-Application-Id": DEV_PARSE_APPLICATION_ID },
    );
    const user = await postJson(
        `${DEV_PARSE_SERVER_URL}/users`,
        {
            authData: {
                bloom: { token: firebase.idToken, id: TEST_ACCOUNT_EMAIL },
            },
            username: TEST_ACCOUNT_EMAIL,
            email: TEST_ACCOUNT_EMAIL,
        },
        "parse-server login",
        { "X-Parse-Application-Id": DEV_PARSE_APPLICATION_ID },
    );
    if (!user.sessionToken || !user.objectId)
        throw new Error(
            `parse-server login for ${TEST_ACCOUNT_EMAIL} returned no session token or user id: ` +
                JSON.stringify(user),
        );
    return {
        email: TEST_ACCOUNT_EMAIL,
        userId: user.objectId,
        sessionToken: user.sessionToken,
    };
}

/**
 * Sign the running Bloom in to Bloom Library as the test account, for real, by posting the login to
 * external/login — the same endpoint the website posts to after a browser login. Bloom saves it in
 * its own user-settings folder, so its bulk-upload child process signs in as this account too, and
 * the top bar shows the account signed in. Returns the login, which the caller keeps so it can
 * delete afterwards what it uploads (see bloomLibraryServer.ts). Uploads go only to the sandbox.
 */
export async function signBloomIntoLibraryForReal(
    page: Page,
): Promise<IBloomLibraryLogin> {
    const login = await getDevBloomLibraryLogin();
    await apiPost(
        page,
        "external/login",
        JSON.stringify({
            sessionToken: login.sessionToken,
            email: login.email,
            userId: login.userId,
        }),
        "application/json",
    );
    return login;
}

/** POST JSON and parse the JSON reply, throwing a diagnostic that names the step on any failure. */
async function postJson(
    url: string,
    body: unknown,
    what: string,
    extraHeaders: Record<string, string> = {},
): Promise<any> {
    let response: Response;
    try {
        response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", ...extraHeaders },
            body: JSON.stringify(body),
        });
    } catch (error) {
        throw new Error(`${what} could not reach ${url}: ${error}`);
    }
    const text = await response.text();
    if (!response.ok)
        throw new Error(`${what} failed (${response.status}): ${text}`);
    try {
        return JSON.parse(text);
    } catch {
        throw new Error(`${what} did not return JSON: ${text}`);
    }
}
