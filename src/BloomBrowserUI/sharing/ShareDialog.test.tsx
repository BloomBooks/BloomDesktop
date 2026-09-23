import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { IShareDialogActions, ShareDialogContents } from "./ShareDialog";
import { ISharingMember, ISharingState } from "./sharingApi";

const now = new Date(2026, 8, 23, 12, 0, 0);
const ruth = "ruth@example.org";

function member(
    email: string,
    role: "admin" | "editor",
    status: "invited" | "active" = "active",
): ISharingMember {
    return {
        email,
        name: status === "active" ? email.split("@")[0] : undefined,
        role,
        status,
        invitedAt: new Date(2026, 8, 19).toISOString(),
        invitedBy: ruth,
        lastSeen:
            status === "active"
                ? new Date(2026, 8, 21).toISOString()
                : undefined,
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
        suggestions: [],
        ...overrides,
    };
}

function makeActions() {
    return {
        invite: vi.fn(() => Promise.resolve()),
        setRole: vi.fn(),
        remove: vi.fn(),
        dismissSuggestions: vi.fn(),
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

    it("before sharing, shows the admin as the sole admin and lets them invite", () => {
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
        expect(emailInput()?.value).toBe("");
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

    it("marks invited people as invited", () => {
        render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("jm@example.org", "editor", "invited"),
                ],
            }),
        );
        expect(findAll("share-member").map((r) => r.dataset.status)).toEqual([
            "active",
            "invited",
        ]);
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

    it("will not let the last admin be demoted or removed", () => {
        render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("amina@example.org", "editor"),
                ],
            }),
        );
        openRoleMenuFor(ruth);
        expect(
            find("share-role-option-editor")?.getAttribute("aria-disabled"),
        ).toBe("true");
        expect(find("share-remove-member")?.getAttribute("aria-disabled")).toBe(
            "true",
        );
        expect(find("share-last-admin-note")).not.toBeNull();
    });

    it("lets an admin demote themselves when there is another admin", () => {
        const actions = render(
            makeState({
                members: [
                    member(ruth, "admin"),
                    member("sam@example.org", "admin"),
                ],
            }),
        );
        openRoleMenuFor(ruth);
        expect(find("share-last-admin-note")).toBeNull();
        click(find("share-role-option-editor"), "the Editor option");
        expect(actions.setRole).toHaveBeenCalledWith(ruth, "editor");
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

    it("invites the checked suggestions, then stops suggesting the unchecked ones", async () => {
        const actions = render(
            makeState({
                isShared: false,
                members: [],
                suggestions: [
                    {
                        email: "sam@example.org",
                        name: "Sam",
                        role: "admin",
                        lastActivity: new Date(2026, 8, 1).toISOString(),
                    },
                    {
                        email: "amina@example.org",
                        name: "Amina",
                        role: "editor",
                        lastActivity: new Date(2026, 7, 1).toISOString(),
                    },
                ],
            }),
        );
        const suggestions = findAll("share-suggestion");
        expect(suggestions.map((s) => s.dataset.email)).toEqual([
            "sam@example.org",
            "amina@example.org",
        ]);
        click(
            suggestions[1].querySelector("input"),
            "Amina's suggestion checkbox",
        );
        click(find("share-invite-suggestions"), "the Invite Selected button");

        expect(actions.invite).toHaveBeenCalledWith([
            { email: "sam@example.org", role: "admin" },
        ]);
        // Dismissing waits for the invitation, which is what shares the collection.
        expect(actions.dismissSuggestions).not.toHaveBeenCalled();
        await act(async () => {});
        expect(actions.dismissSuggestions).toHaveBeenCalledWith([
            "amina@example.org",
        ]);
    });

    it("does not dismiss anyone when every suggestion is invited", async () => {
        const actions = render(
            makeState({
                suggestions: [
                    {
                        email: "sam@example.org",
                        role: "editor",
                        lastActivity: new Date(2026, 8, 1).toISOString(),
                    },
                ],
            }),
        );
        click(find("share-invite-suggestions"), "the Invite Selected button");
        expect(actions.invite).toHaveBeenCalledTimes(1);
        await act(async () => {});
        expect(actions.dismissSuggestions).not.toHaveBeenCalled();
    });
});
