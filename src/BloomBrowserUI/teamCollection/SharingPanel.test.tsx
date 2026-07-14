import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { SharingMembersList } from "./SharingPanel";
import { IApprovedMember } from "./sharingApi";

// Tests the presentational SharingMembersList directly with injected members/callbacks
// (no network layer), per Wave-1 scope: shells against mocked endpoints.

let renderedContainer: HTMLDivElement | undefined;

function render(element: React.ReactElement): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(element, container);
    });
    return container;
}

// React tracks the previous value of native inputs/selects internally; setting .value directly
// and dispatching a plain Event doesn't trigger React's onChange. This is the standard
// workaround (there is no @testing-library/react / user-event dependency in this project).
function setNativeValue(
    element: HTMLInputElement | HTMLSelectElement,
    value: string,
) {
    const prototype =
        element instanceof HTMLSelectElement
            ? window.HTMLSelectElement.prototype
            : window.HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
    element.dispatchEvent(new Event("input", { bubbles: true }));
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
});

const claimedAdmin: IApprovedMember = {
    email: "admin@example.com",
    name: "Ada Admin",
    role: "admin",
    claimed: true,
};
const pendingMember: IApprovedMember = {
    email: "pending@example.com",
    role: "member",
    claimed: false,
};
const secondAdmin: IApprovedMember = {
    email: "second-admin@example.com",
    name: "Bea Boss",
    role: "admin",
    claimed: true,
};

function renderList(
    members: IApprovedMember[],
    overrides: {
        isAdmin?: boolean;
        onAdd?: (email: string, role: "admin" | "member") => void;
        onRemove?: (email: string) => void;
        onSetRole?: (email: string, role: "admin" | "member") => void;
        onSetDisplayName?: (email: string, name: string) => void;
    } = {},
) {
    const onAdd = overrides.onAdd ?? vi.fn();
    const onRemove = overrides.onRemove ?? vi.fn();
    const onSetRole = overrides.onSetRole ?? vi.fn();
    const onSetDisplayName = overrides.onSetDisplayName ?? vi.fn();
    const container = render(
        <SharingMembersList
            members={members}
            currentUserEmail={claimedAdmin.email}
            isAdmin={overrides.isAdmin ?? true}
            onAdd={onAdd}
            onRemove={onRemove}
            onSetRole={onSetRole}
            onSetDisplayName={onSetDisplayName}
        />,
    );
    return { container, onAdd, onRemove, onSetRole, onSetDisplayName };
}

// The name edit commits on Enter (and blur) and cancels on Escape.
function pressKey(element: HTMLElement, key: string) {
    element.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true }));
}

function rowFor(container: HTMLElement, email: string): HTMLElement {
    const row = Array.from(
        container.querySelectorAll('[data-testid="sharing-member-row"]'),
    ).find((r) => r.getAttribute("data-email") === email) as HTMLElement;
    expect(row).not.toBeUndefined();
    return row;
}

describe("SharingMembersList", () => {
    it("renders one row per member, with claimed/pending status", () => {
        const { container } = renderList([claimedAdmin, pendingMember]);

        const rows = container.querySelectorAll(
            '[data-testid="sharing-member-row"]',
        );
        expect(rows.length).toBe(2);

        // We check the data-claimed flag rather than the (localized) chip text, since
        // localization in tests resolves to the l10n key rather than the English text.
        const statuses = Array.from(
            container.querySelectorAll('[data-testid="sharing-member-status"]'),
        ).map((el) => el.getAttribute("data-claimed"));
        expect(statuses).toEqual(["true", "false"]);
    });

    it("read-only view (non-admin) hides add/remove/role controls", () => {
        const { container } = renderList([claimedAdmin, pendingMember], {
            isAdmin: false,
        });

        expect(
            container.querySelector('[data-testid="sharing-add-row"]'),
        ).toBeNull();
        expect(
            container.querySelector('[data-testid="sharing-remove-button"]'),
        ).toBeNull();
        expect(
            container.querySelector('[data-testid="sharing-role-select"]'),
        ).toBeNull();
    });

    it("admin can add a member with a chosen role", () => {
        const { container, onAdd } = renderList([claimedAdmin]);

        const emailInput = container.querySelector(
            '[data-testid="sharing-add-email-input"] input',
        ) as HTMLInputElement;
        expect(emailInput).not.toBeNull();

        act(() => setNativeValue(emailInput, "newperson@example.com"));

        const roleSelect = container.querySelector(
            '[data-testid="sharing-add-role-select"]',
        ) as HTMLSelectElement;
        act(() => setNativeValue(roleSelect, "admin"));

        const addButton = container.querySelector(
            '[data-testid="sharing-add-button"]',
        ) as HTMLButtonElement;
        act(() => addButton.click());

        expect(onAdd).toHaveBeenCalledWith("newperson@example.com", "admin");
    });

    it("rejects an invalid email and does not call onAdd", () => {
        const { container, onAdd } = renderList([claimedAdmin]);

        const emailInput = container.querySelector(
            '[data-testid="sharing-add-email-input"] input',
        ) as HTMLInputElement;
        act(() => setNativeValue(emailInput, "not-an-email"));

        const addButton = container.querySelector(
            '[data-testid="sharing-add-button"]',
        ) as HTMLButtonElement;
        act(() => addButton.click());

        expect(onAdd).not.toHaveBeenCalled();
    });

    it("remove requires confirmation before calling onRemove", () => {
        const { container, onRemove } = renderList([
            claimedAdmin,
            pendingMember,
        ]);

        const rows = container.querySelectorAll(
            '[data-testid="sharing-member-row"]',
        );
        const pendingRow = Array.from(rows).find(
            (row) => row.getAttribute("data-email") === pendingMember.email,
        ) as HTMLElement;
        const removeButton = pendingRow.querySelector(
            '[data-testid="sharing-remove-button"]',
        ) as HTMLButtonElement;

        act(() => removeButton.click());
        expect(onRemove).not.toHaveBeenCalled();
        expect(
            container.querySelector(
                '[data-testid="sharing-remove-confirmation"]',
            ),
        ).not.toBeNull();

        const confirmButton = container.querySelector(
            '[data-testid="sharing-confirm-remove-button"]',
        ) as HTMLButtonElement;
        act(() => confirmButton.click());

        expect(onRemove).toHaveBeenCalledWith(pendingMember.email);
    });

    it("protects the last remaining admin from being demoted or removed", () => {
        const { container, onSetRole } = renderList([
            claimedAdmin,
            pendingMember,
        ]);

        const rows = container.querySelectorAll(
            '[data-testid="sharing-member-row"]',
        );
        const adminRow = Array.from(rows).find(
            (row) => row.getAttribute("data-email") === claimedAdmin.email,
        ) as HTMLElement;
        const roleSelect = adminRow.querySelector(
            '[data-testid="sharing-role-select"]',
        ) as HTMLSelectElement;
        const removeButton = adminRow.querySelector(
            '[data-testid="sharing-remove-button"]',
        ) as HTMLButtonElement;

        expect(roleSelect.disabled).toBe(true);
        expect(removeButton.disabled).toBe(true);

        // Sanity check: changing its value while disabled must not fire a change we'd act on.
        act(() => setNativeValue(roleSelect, "member"));
        expect(onSetRole).not.toHaveBeenCalled();
    });

    it("allows demoting an admin when another admin remains", () => {
        const { container, onSetRole } = renderList([
            claimedAdmin,
            secondAdmin,
        ]);

        const rows = container.querySelectorAll(
            '[data-testid="sharing-member-row"]',
        );
        const adminRow = Array.from(rows).find(
            (row) => row.getAttribute("data-email") === claimedAdmin.email,
        ) as HTMLElement;
        const roleSelect = adminRow.querySelector(
            '[data-testid="sharing-role-select"]',
        ) as HTMLSelectElement;

        expect(roleSelect.disabled).toBe(false);
        act(() => setNativeValue(roleSelect, "member"));

        expect(onSetRole).toHaveBeenCalledWith(claimedAdmin.email, "member");
    });

    it("admin can edit a member's display name (commit with Enter)", () => {
        const { container, onSetDisplayName } = renderList([
            claimedAdmin,
            pendingMember,
        ]);

        const row = rowFor(container, pendingMember.email);
        const editButton = row.querySelector(
            '[data-testid="sharing-edit-name-button"]',
        ) as HTMLButtonElement;
        expect(editButton).not.toBeNull();

        act(() => editButton.click());
        const input = row.querySelector(
            '[data-testid="sharing-name-input"]',
        ) as HTMLInputElement;
        // Sanity: pendingMember has no name yet, so the edit starts empty.
        expect(input.value).toBe("");

        act(() => setNativeValue(input, "  Penny Pending  "));
        act(() => pressKey(input, "Enter"));

        // Trimmed before committing.
        expect(onSetDisplayName).toHaveBeenCalledWith(
            pendingMember.email,
            "Penny Pending",
        );
        // The edit closes back to display mode.
        expect(
            row.querySelector('[data-testid="sharing-name-input"]'),
        ).toBeNull();
    });

    it("Escape cancels a name edit without committing", () => {
        const { container, onSetDisplayName } = renderList([claimedAdmin]);

        const row = rowFor(container, claimedAdmin.email);
        const editButton = row.querySelector(
            '[data-testid="sharing-edit-name-button"]',
        ) as HTMLButtonElement;
        act(() => editButton.click());

        const input = row.querySelector(
            '[data-testid="sharing-name-input"]',
        ) as HTMLInputElement;
        // The edit starts from the current name.
        expect(input.value).toBe(claimedAdmin.name);

        act(() => setNativeValue(input, "Changed But Abandoned"));
        act(() => pressKey(input, "Escape"));

        expect(onSetDisplayName).not.toHaveBeenCalled();
        expect(
            row.querySelector('[data-testid="sharing-name-input"]'),
        ).toBeNull();
    });

    it("committing an unchanged name does not call onSetDisplayName", () => {
        const { container, onSetDisplayName } = renderList([claimedAdmin]);

        const row = rowFor(container, claimedAdmin.email);
        act(() =>
            (
                row.querySelector(
                    '[data-testid="sharing-edit-name-button"]',
                ) as HTMLButtonElement
            ).click(),
        );
        const input = row.querySelector(
            '[data-testid="sharing-name-input"]',
        ) as HTMLInputElement;
        act(() => pressKey(input, "Enter"));

        expect(onSetDisplayName).not.toHaveBeenCalled();
    });

    it("emptying the name commits an empty string (clears it)", () => {
        const { container, onSetDisplayName } = renderList([claimedAdmin]);

        const row = rowFor(container, claimedAdmin.email);
        act(() =>
            (
                row.querySelector(
                    '[data-testid="sharing-edit-name-button"]',
                ) as HTMLButtonElement
            ).click(),
        );
        const input = row.querySelector(
            '[data-testid="sharing-name-input"]',
        ) as HTMLInputElement;
        act(() => setNativeValue(input, "   "));
        act(() => pressKey(input, "Enter"));

        expect(onSetDisplayName).toHaveBeenCalledWith(claimedAdmin.email, "");
    });

    it("non-admins get no name-edit affordance", () => {
        const { container } = renderList([claimedAdmin, pendingMember], {
            isAdmin: false,
        });

        expect(
            container.querySelector('[data-testid="sharing-edit-name-button"]'),
        ).toBeNull();
        // ...but an existing name still displays.
        expect(rowFor(container, claimedAdmin.email).textContent).toContain(
            claimedAdmin.name,
        );
    });
});
