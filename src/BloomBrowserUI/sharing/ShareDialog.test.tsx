import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { IShareDialogActions, ShareDialogContents } from "./ShareDialog";
import { ISharingMember, ISharingState } from "./sharingApi";

// Unmocked, useL10n gives back the key in tests; the English (with its parameter filled in)
// lets tests check what a string says.
vi.mock("../react_components/l10nHooks", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../react_components/l10nHooks")>()),
    useL10n: (
        english: string,
        _key: string | null,
        _comment?: string,
        param0?: string,
    ) => english.replace("%0", param0 ?? ""),
}));

const now = new Date(2026, 8, 23, 12, 0, 0);
const ruth = "ruth@example.org";

// A member invited on September 19th, who (if seen) last used the collection on the 21st.
function member(
    email: string,
    role: "admin" | "editor",
    seen = true,
): ISharingMember {
    return {
        email,
        name: seen ? email.split("@")[0] : undefined,
        role,
        invitedAt: new Date(2026, 8, 19).toISOString(),
        invitedBy: ruth,
        lastSeen: seen ? new Date(2026, 8, 21).toISOString() : undefined,
    };
}

function makeState(overrides: Partial<ISharingState>): ISharingState {
    return {
        collectionName: "Bantu Readers",
        signedInEmail: ruth,
        signedInName: "Ruth Nakalema",
        isShared: true,
        canManage: true,
        members: [member(ruth, "admin")],
        ...overrides,
    };
}

function makeActions() {
    return {
        invite: vi.fn(() => Promise.resolve(true)),
        setRole: vi.fn(),
        remove: vi.fn(),
        signIn: vi.fn(),
    } satisfies IShareDialogActions;
}

let container: HTMLDivElement | undefined;

function render(state: ISharingState, actions = makeActions()) {
    container = document.createElement("div");
    document.body.appendChild(container);
    const target = container;
    act(() => {
        renderRoot(
            <ShareDialogContents
                state={state}
                actions={actions}
                uiLanguage="en"
                now={now}
                onClose={() => {}}
            />,
            target,
        );
    });
    return actions;
}

afterEach(() => {
    if (container) {
        act(() => unmountRoot(container!));
        container.remove();
        container = undefined;
    }
    // MUI menus render in portals on document.body; clear any left behind.
    document.body.innerHTML = "";
});

function find(testId: string, root: ParentNode = document.body) {
    return root.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
}

function findAll(testId: string) {
    return Array.from(
        document.body.querySelectorAll<HTMLElement>(
            `[data-testid="${testId}"]`,
        ),
    );
}

function click(element: HTMLElement | null, what: string) {
    if (!element) fail(`Could not find ${what} to click.`);
    act(() => {
        element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
}

// Types into a React-controlled input by going through the native value setter, which is what
// React listens for.
function typeInto(input: HTMLInputElement | null, text: string) {
    if (!input) fail("Could not find the email input.");
    const setter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        "value",
    )!.set!;
    act(() => {
        setter.call(input, text);
        input.dispatchEvent(new Event("input", { bubbles: true }));
    });
}

function emailInput() {
    return find("share-invite-email")?.querySelector("input") ?? null;
}

function inviteButton() {
    return find("share-invite-button") as HTMLButtonElement | null;
}

function openRoleMenuFor(email: string) {
    const row = document.body.querySelector<HTMLElement>(
        `[data-testid="share-member"][data-email="${email}"]`,
    );
    if (!row) fail(`No member row for ${email}.`);
    click(find("share-member-role", row), `the role button for ${email}`);
}

describe("ShareDialogContents", () => {
    it("asks someone who is not signed in to sign in, and offers no invite row", () => {
        const actions = render(
            makeState({ signedInEmail: "", canManage: false, members: [] }),
        );
        expect(find("share-invite-row")).toBeNull();
        click(
            find("share-sign-in")?.querySelector("button") ?? null,
            "the Sign In button",
        );
        expect(actions.signIn).toHaveBeenCalledTimes(1);
    });

    it("before sharing, shows the admin as the sole admin and lets them invite", async () => {
        const actions = render(makeState({ isShared: false, members: [] }));
        const rows = findAll("share-member");
        expect(rows.map((r) => r.dataset.email)).toEqual([ruth]);

        expect(inviteButton()?.disabled).toBe(true);
        typeInto(emailInput(), " amina@example.org ");
        expect(inviteButton()?.disabled).toBe(false);
        click(inviteButton(), "the Invite button");

        expect(actions.invite).toHaveBeenCalledWith([
            { email: "amina@example.org", role: "editor" },
        ]);
        // While the invitation is on its way, it can't be sent again, and nothing new can
        // be typed for the successful result to wipe out.
        expect(inviteButton()?.disabled).toBe(true);
        expect(emailInput()?.disabled).toBe(true);
        await act(async () => {});
        expect(emailInput()?.value).toBe("");
    });

    it("keeps the typed email if the invitation fails", async () => {
        const actions = makeActions();
        actions.invite.mockImplementation(() => Promise.resolve(false));
        render(makeState({}), actions);
        typeInto(emailInput(), "amina@example.org");
        click(inviteButton(), "the Invite button");
        await act(async () => {});
        expect(actions.invite).toHaveBeenCalledTimes(1);
        expect(emailInput()?.value).toBe("amina@example.org");
        expect(inviteButton()?.disabled).toBe(false);
    });

    it("does not invite something that is not an email address", () => {
        const actions = render(makeState({}));
        typeInto(emailInput(), "amina");
        click(inviteButton(), "the Invite button");
        expect(actions.invite).not.toHaveBeenCalled();
        expect(emailInput()?.getAttribute("aria-invalid")).toBe("true");
    });

    it("will not invite someone who already has access", () => {
        const actions = render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("amina@example.org", "editor"),
                ],
            }),
        );
        typeInto(emailInput(), "AMINA@example.org");
        expect(inviteButton()?.disabled).toBe(true);
        expect(emailInput()?.getAttribute("aria-invalid")).toBe("true");
        expect(actions.invite).not.toHaveBeenCalled();
    });

    it("can invite as an admin", () => {
        const actions = render(makeState({}));
        typeInto(emailInput(), "sam@example.org");
        click(find("share-invite-role"), "the invite role button");
        click(find("share-role-option-admin"), "the Admin option");
        click(inviteButton(), "the Invite button");
        expect(actions.invite).toHaveBeenCalledWith([
            { email: "sam@example.org", role: "admin" },
        ]);
    });

    it("shows when each person was last seen, or, if never, when they were invited", () => {
        const neverSeen = member("jm@example.org", "editor", false);
        // Sanity check: the test data really does distinguish the two.
        expect(member(ruth, "admin").lastSeen).toBeDefined();
        expect(neverSeen.lastSeen).toBeUndefined();
        render(makeState({ members: [member(ruth, "admin"), neverSeen] }));
        const when = findAll("share-member").map(
            (row) => find("share-member-when", row)?.textContent,
        );
        // now is September 23rd; seen on the 21st, invited on the 19th.
        expect(when).toEqual(["Last seen 2 days ago", "Invited 4 days ago"]);
    });

    it("lets an admin change a role and remove someone", () => {
        const actions = render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("amina@example.org", "editor"),
                ],
            }),
        );
        openRoleMenuFor("amina@example.org");
        click(find("share-role-option-admin"), "the Admin option");
        expect(actions.setRole).toHaveBeenCalledWith(
            "amina@example.org",
            "admin",
        );

        openRoleMenuFor("amina@example.org");
        click(find("share-remove-member"), "the Remove item");
        expect(actions.remove).toHaveBeenCalledWith("amina@example.org");
    });

    it("does not report choosing the role someone already has", () => {
        const actions = render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("amina@example.org", "editor"),
                ],
            }),
        );
        openRoleMenuFor("amina@example.org");
        click(find("share-role-option-editor"), "the Editor option");
        expect(actions.setRole).not.toHaveBeenCalled();
    });

    it("never lets an admin change their own role, even with another admin", () => {
        const actions = render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("sam@example.org", "admin"),
                ],
            }),
        );
        const ownRow = document.body.querySelector<HTMLElement>(
            `[data-testid="share-member"][data-email="${ruth}"]`,
        );
        if (!ownRow) fail("No row for the signed-in admin.");
        expect(find("share-member-role", ownRow)).toBeNull();
        const fixedRole = find("share-member-fixed-role", ownRow);
        expect(fixedRole?.getAttribute("title")).toBeTruthy();

        // The other admin can still be changed, which is how someone steps down.
        openRoleMenuFor("sam@example.org");
        click(find("share-role-option-editor"), "the Editor option");
        expect(actions.setRole).toHaveBeenCalledWith(
            "sam@example.org",
            "editor",
        );
    });

    it("links to the sharing help page", () => {
        render(makeState({}));
        expect(find("share-learn-link")?.getAttribute("href")).toBe(
            "https://docs.bloomlibrary.org/team-collections-intro/",
        );
    });
    it("shows a non-admin the list without any way to change it", () => {
        render(
            makeState({
                signedInEmail: "amina@example.org",
                canManage: false,
                members: [
                    member(ruth, "admin"),
                    member("amina@example.org", "editor"),
                ],
            }),
        );
        expect(findAll("share-member")).toHaveLength(2);
        expect(find("share-only-admins")).not.toBeNull();
        expect(find("share-invite-row")).toBeNull();
        expect(find("share-member-role")).toBeNull();
    });
});
