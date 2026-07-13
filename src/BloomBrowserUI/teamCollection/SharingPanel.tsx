import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import Chip from "@mui/material/Chip";
import IconButton from "@mui/material/IconButton";
import DeleteIcon from "@mui/icons-material/Delete";
import { BloomAvatar } from "../react_components/bloomAvatar";
import BloomButton from "../react_components/bloomButton";
import { AttentionTextField } from "../react_components/AttentionTextField";
import { Div, Span } from "../react_components/l10nComponents";
import { useL10n } from "../react_components/l10nHooks";
import { WarningBox } from "../react_components/boxes";
import { isValidEmail } from "../utils/emailUtils";
import { kBloomGray } from "../utils/colorUtils";
import {
    IApprovedMember,
    SharingRole,
    addApproval,
    removeApproval,
    setRole as setMemberRole,
    useSharingMembers,
} from "./sharingApi";

// The Sharing panel for cloud Team Collections: shown in the Team Collection settings panel
// in place of the old free-text administrator-emails field. Folder Team Collections keep the
// old panel (see TeamCollectionSettingsPanel.tsx); this only applies when the collection is
// backed by the cloud (S3 + Supabase) repo.

// Container: wires the presentational list up to the (Wave-3) SharingApi endpoints.
export const SharingPanel: React.FunctionComponent<{
    collectionId: string;
    currentUserEmail: string;
    isAdmin: boolean;
}> = (props) => {
    const { members, reload } = useSharingMembers(props.collectionId);
    return (
        <SharingMembersList
            members={members}
            currentUserEmail={props.currentUserEmail}
            isAdmin={props.isAdmin}
            onAdd={(email, role) => {
                addApproval(props.collectionId, email, role).then(reload);
            }}
            onRemove={(email) => {
                removeApproval(props.collectionId, email).then(reload);
            }}
            onSetRole={(email, role) => {
                setMemberRole(props.collectionId, email, role).then(reload);
            }}
        />
    );
};

// Presentational: pure function of its props, so it can be unit-tested without any network layer.
export const SharingMembersList: React.FunctionComponent<{
    members: IApprovedMember[];
    currentUserEmail: string;
    isAdmin: boolean;
    onAdd: (email: string, role: SharingRole) => void;
    onRemove: (email: string) => void;
    onSetRole: (email: string, role: SharingRole) => void;
}> = (props) => {
    const adminCount = props.members.filter((m) => m.role === "admin").length;

    return (
        <div
            data-testid="sharing-panel"
            css={css`
                display: flex;
                flex-direction: column;
            `}
        >
            {!props.isAdmin && (
                <Div
                    l10nKey="TeamCollection.Sharing.ReadOnlyNote"
                    temporarilyDisableI18nWarning={true}
                    css={css`
                        margin-bottom: 8px;
                        color: ${kBloomGray};
                    `}
                >
                    Only administrators can add, remove, or change the role of
                    team members.
                </Div>
            )}
            <div
                data-testid="sharing-member-rows"
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 6px;
                `}
            >
                {props.members.map((member) => (
                    <MemberRow
                        key={member.email}
                        member={member}
                        isAdmin={props.isAdmin}
                        isLastAdmin={
                            member.role === "admin" && adminCount === 1
                        }
                        onRemove={() => props.onRemove(member.email)}
                        onSetRole={(role) =>
                            props.onSetRole(member.email, role)
                        }
                    />
                ))}
            </div>
            {props.isAdmin && (
                <AddMemberRow
                    onAdd={(email, role) => props.onAdd(email, role)}
                />
            )}
        </div>
    );
};

// A plain HTML <select> rather than MUI's Select: MUI's Select renders its options in a
// portal, which is awkward to drive from the raw-DOM-event vitest tests we use elsewhere in
// this codebase (there is no @testing-library/react or user-event dependency here). A plain
// <select> keeps the CRUD flows in SharingPanel.test.tsx simple and robust.
const RoleSelect: React.FunctionComponent<{
    value: SharingRole;
    disabled?: boolean;
    adminLabel: string;
    memberLabel: string;
    "data-testid": string;
    onChange: (role: SharingRole) => void;
}> = (props) => {
    return (
        <select
            data-testid={props["data-testid"]}
            value={props.value}
            disabled={props.disabled}
            css={css`
                font-family: inherit;
                font-size: inherit;
                padding: 2px 4px;
                // Match the small MUI Chip's height so the role control sits on the same
                // visual line as the Claimed/Pending indicator in a member row.
                height: 24px;
                box-sizing: border-box;
            `}
            onChange={(event) => {
                // Guard against synthetic/programmatic change events reaching a select that's
                // supposed to be locked (e.g. the sole remaining admin's role); a real browser
                // already blocks user interaction with a disabled <select>.
                if (props.disabled) return;
                props.onChange(event.target.value as SharingRole);
            }}
        >
            <option value="admin">{props.adminLabel}</option>
            <option value="member">{props.memberLabel}</option>
        </select>
    );
};

const MemberRow: React.FunctionComponent<{
    member: IApprovedMember;
    isAdmin: boolean;
    isLastAdmin: boolean;
    onRemove: () => void;
    onSetRole: (role: SharingRole) => void;
}> = (props) => {
    const [confirmingRemove, setConfirmingRemove] = useState(false);
    const claimedLabel = useL10n(
        "Claimed",
        "TeamCollection.Sharing.Claimed",
        undefined,
        undefined,
        undefined,
        true,
    );
    const pendingLabel = useL10n(
        "Pending",
        "TeamCollection.Sharing.Pending",
        "Shown next to an approved email address that no one has signed in with yet.",
        undefined,
        undefined,
        true,
    );
    const adminLabel = useL10n(
        "Admin",
        "TeamCollection.Sharing.RoleAdmin",
        undefined,
        undefined,
        undefined,
        true,
    );
    const memberLabel = useL10n(
        "Member",
        "TeamCollection.Sharing.RoleMember",
        undefined,
        undefined,
        undefined,
        true,
    );
    const lastAdminTooltip = useL10n(
        "A Team Collection must always have at least one administrator.",
        "TeamCollection.Sharing.LastAdminProtection",
        undefined,
        undefined,
        undefined,
        true,
    );

    return (
        <div
            data-testid="sharing-member-row"
            data-email={props.member.email}
            css={css`
                display: flex;
                align-items: center;
                gap: 8px;
                // The settings dialog's page-level styles can add vertical margins/padding to
                // controls, which defeats the flex centering. Two known offenders (found with
                // the inspector, 13 Jul): stray margins on select/button, and a margin UNDER
                // the MUI Chip (its outer box centers, so the visible pill rides high).
                // Neutralize both so every control truly centers on the row.
                select,
                button {
                    margin-top: 0;
                    margin-bottom: 0;
                    align-self: center;
                }
                .MuiChip-root {
                    margin-top: 0;
                    margin-bottom: 0;
                    padding-top: 0;
                    padding-bottom: 0;
                }
                .MuiChip-root .MuiChip-label {
                    padding-top: 0;
                    padding-bottom: 0;
                }
            `}
        >
            <BloomAvatar
                email={props.member.email}
                name={props.member.name || props.member.email}
                avatarSizeInt={28}
            />
            <div
                css={css`
                    flex-grow: 1;
                    display: flex;
                    flex-direction: column;
                `}
            >
                {props.member.name && <span>{props.member.name}</span>}
                <span
                    css={css`
                        color: ${kBloomGray};
                        font-size: 0.9em;
                    `}
                >
                    {props.member.email}
                </span>
            </div>
            <Chip
                size="small"
                label={props.member.claimed ? claimedLabel : pendingLabel}
                data-testid="sharing-member-status"
                data-claimed={props.member.claimed}
                color={props.member.claimed ? "success" : "default"}
            />
            {props.isAdmin ? (
                <RoleSelect
                    value={props.member.role}
                    disabled={props.isLastAdmin}
                    adminLabel={adminLabel}
                    memberLabel={memberLabel}
                    data-testid="sharing-role-select"
                    onChange={props.onSetRole}
                />
            ) : (
                <Chip
                    size="small"
                    label={
                        props.member.role === "admin" ? adminLabel : memberLabel
                    }
                />
            )}
            {props.isAdmin && (
                <>
                    <IconButton
                        size="small"
                        disabled={props.isLastAdmin}
                        title={props.isLastAdmin ? lastAdminTooltip : undefined}
                        data-testid="sharing-remove-button"
                        css={css`
                            // Shrink the default touch padding so the trash can lines up
                            // with the chip and role select instead of hanging below them.
                            padding: 2px;
                        `}
                        onClick={() => setConfirmingRemove(true)}
                    >
                        <DeleteIcon fontSize="small" />
                    </IconButton>
                    {confirmingRemove && (
                        <RemoveConfirmation
                            email={props.member.email}
                            onCancel={() => setConfirmingRemove(false)}
                            onConfirm={() => {
                                setConfirmingRemove(false);
                                props.onRemove();
                            }}
                        />
                    )}
                </>
            )}
        </div>
    );
};

const RemoveConfirmation: React.FunctionComponent<{
    email: string;
    onCancel: () => void;
    onConfirm: () => void;
}> = (props) => {
    return (
        <div
            data-testid="sharing-remove-confirmation"
            css={css`
                position: absolute;
                z-index: 1;
                margin-top: 40px;
            `}
        >
            <WarningBox>
                <Span
                    l10nKey="TeamCollection.Sharing.RemoveWarning"
                    l10nParam0={props.email}
                    temporarilyDisableI18nWarning={true}
                >
                    Removing %0 will immediately force-unlock any books they
                    currently have checked out. Continue?
                </Span>
                <div
                    css={css`
                        display: flex;
                        gap: 8px;
                        margin-top: 8px;
                    `}
                >
                    <BloomButton
                        size="small"
                        variant="contained"
                        color="error"
                        enabled={true}
                        hasText={true}
                        l10nKey="TeamCollection.Sharing.ConfirmRemove"
                        temporarilyDisableI18nWarning={true}
                        data-testid="sharing-confirm-remove-button"
                        onClick={props.onConfirm}
                    >
                        Remove
                    </BloomButton>
                    <BloomButton
                        size="small"
                        variant="text"
                        enabled={true}
                        hasText={true}
                        l10nKey="Common.Cancel"
                        onClick={props.onCancel}
                    >
                        Cancel
                    </BloomButton>
                </div>
            </WarningBox>
        </div>
    );
};

const AddMemberRow: React.FunctionComponent<{
    onAdd: (email: string, role: SharingRole) => void;
}> = (props) => {
    const [email, setEmail] = useState("");
    const [role, setRoleValue] = useState<SharingRole>("member");
    const [submitAttempts, setSubmitAttempts] = useState(0);
    const memberLabel = useL10n(
        "Member",
        "TeamCollection.Sharing.RoleMember",
        undefined,
        undefined,
        undefined,
        true,
    );
    const adminLabel = useL10n(
        "Admin",
        "TeamCollection.Sharing.RoleAdmin",
        undefined,
        undefined,
        undefined,
        true,
    );

    const tryAdd = () => {
        if (!isValidEmail(email.trim())) {
            setSubmitAttempts((old) => old + 1);
            return;
        }
        props.onAdd(email.trim(), role);
        setEmail("");
        setSubmitAttempts(0);
    };

    return (
        <div
            data-testid="sharing-add-row"
            css={css`
                display: flex;
                // Top-align and offset each control to the vertical center of the email
                // input's 40px box (not the row's center): the AttentionTextField grows
                // downward when it shows a validation message, and center alignment would
                // drag the role select and Add button down with it.
                align-items: flex-start;
                gap: 8px;
                margin-top: 12px;
                select {
                    margin-top: 8px;
                }
                button {
                    margin-top: 2px;
                }
            `}
        >
            <AttentionTextField
                size="small"
                label="Email address"
                l10nKey="TeamCollection.Sharing.EmailAddress"
                // Note: unlike Div/P/BloomButton, AttentionTextField's underlying MuiTextField
                // treats temporarilyDisableI18nWarning as "skip the XLF lookup entirely" (see
                // muiTextField.tsx), not just "suppress the warning" — so it must be omitted
                // here for this label to actually be localized.
                value={email}
                onChange={setEmail}
                isValid={(value) => isValidEmail(value.trim())}
                submitAttempts={submitAttempts}
                data-testid="sharing-add-email-input"
            />
            <RoleSelect
                value={role}
                adminLabel={adminLabel}
                memberLabel={memberLabel}
                data-testid="sharing-add-role-select"
                onChange={setRoleValue}
            />
            <BloomButton
                enabled={true}
                hasText={true}
                variant="outlined"
                l10nKey="TeamCollection.Sharing.AddMember"
                temporarilyDisableI18nWarning={true}
                data-testid="sharing-add-button"
                onClick={tryAdd}
            >
                Add
            </BloomButton>
        </div>
    );
};
