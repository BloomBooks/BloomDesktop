// The user's registration with Bloom: the name, email address, organization and use that
// Help > Registration asks for.
//
// Bloom keeps the registration in Registration.json in the user's own application data folder
// (ProjectContext.AppDataPath), which the launch fixture does NOT give each Bloom a copy of, as it
// does for user.config. So the Bloom under test reads and writes the registration of whoever runs
// the suite. A test that needs a registration therefore uses withRegistration, which puts back
// what was there.

import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";

/** What a person tells Bloom in its Registration dialog. */
export interface IRegistration {
    firstName: string;
    surname: string;
    email: string;
    organization: string;
    usingFor: string;
}

const kUserInfoEndpoint = "registration/userInfo";

/** The registration that Bloom holds now. A field nobody filled in is the empty string. */
export async function getRegistration(page: Page): Promise<IRegistration> {
    const info = await apiGetJson<Partial<IRegistration>>(
        page,
        kUserInfoEndpoint,
    );
    return {
        firstName: info.firstName ?? "",
        surname: info.surname ?? "",
        email: info.email ?? "",
        organization: info.organization ?? "",
        usingFor: info.usingFor ?? "",
    };
}

/**
 * Save `registration` as the Registration dialog's Register button does, without the dialog, and
 * return once Bloom reports it. This is the route for setup; it replaces the registration of
 * whoever runs the suite, so call it through withRegistration.
 */
async function setRegistration(
    page: Page,
    registration: IRegistration,
): Promise<void> {
    await apiPost(
        page,
        kUserInfoEndpoint,
        // hadEmailAlready keeps Bloom from counting this as a new person registering.
        JSON.stringify({ ...registration, hadEmailAlready: true }),
        "application/json",
    );
    await expect
        .poll(() => getRegistration(page), {
            message: "Bloom never reported the registration that was saved.",
        })
        .toEqual(registration);
}

/**
 * Run `body` in a Bloom registered as `registration`, and return what it returns. Whatever
 * registration Bloom held before is put back afterwards, whether or not `body` succeeded.
 */
export async function withRegistration<T>(
    page: Page,
    registration: IRegistration,
    body: () => Promise<T>,
): Promise<T> {
    const before = await getRegistration(page);
    await setRegistration(page, registration);
    try {
        return await body();
    } finally {
        await setRegistration(page, before);
    }
}
