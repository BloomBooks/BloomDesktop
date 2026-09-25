import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import ArrowDropUpIcon from "@mui/icons-material/ArrowDropUp";
import CheckIcon from "@mui/icons-material/Check";
import HelpOutlineIcon from "@mui/icons-material/HelpOutline";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../react_components/BloomDialog/commonDialogComponents";
import BloomButton from "../react_components/bloomButton";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { useL10n } from "../react_components/l10nHooks";
import { useLoginState } from "../react_components/useLoginState";
import {
    kBannerGray,
    kBloomBlue,
    kBloomRed,
    kMutedTextGray,
} from "../bloomMaterialUITheme";
import { useApiString } from "../utils/bloomApi";
import { kFormBackground } from "../utils/colorUtils";
import { isValidEmail } from "../utils/emailUtils";
import { formatTimeAgo } from "./relativeTime";
import {
    IInvitation,
    ISharingMember,
    ISharingState,
    SharingRole,
    invite,
    removeMember,
    sameEmail,
    setRole,
    useSharingState,
} from "./sharingApi";

// What the dialog asks the server to do. Separate from the component so tests can supply fakes.
export interface IShareDialogActions {
    // Resolves once the server has answered: true if it made the invitations (and so shared
    // the collection), false if it failed (the failure has already been reported).
    invite: (invitations: IInvitation[]) => Promise<boolean>;
    setRole: (email: string, role: SharingRole) => void;
    remove: (email: string) => void;
    signIn: () => void;
}

// The dialog the Collection tab's Share button opens: who has access to this collection, and,
// for its admins, inviting people and changing what they may do.
export const ShareDialog: React.FunctionComponent<{
    open: boolean;
    onClose: () => void;
}> = (props) => {
    const login = useLoginState();
    return (
        <BloomDialog
            open={props.open}
            onClose={props.onClose}
            onCancel={props.onClose}
            maxWidth="sm"
            fullWidth={true}
        >
            {props.open && (
                // Keyed on the sign-in so that signing in or out (e.g. with the dialog's own
                // Sign In button) fetches the state afresh for the new person.
                <ShareDialogWithData
                    key={login.email ?? ""}
                    onClose={props.onClose}
                    signIn={login.signIn}
                />
            )}
        </BloomDialog>
    );
};

const ShareDialogWithData: React.FunctionComponent<{
    onClose: () => void;
    signIn: () => void;
}> = (props) => {
    const state = useSharingState();
    const uiLanguage = useApiString("currentUiLanguage", "en");
    if (!state) return null;
    return (
        <ShareDialogContents
            state={state}
            actions={{
                invite,
                setRole,
                remove: removeMember,
                signIn: props.signIn,
            }}
            uiLanguage={uiLanguage}
            now={new Date()}
            onClose={props.onClose}
        />
    );
};

// Everything inside the dialog frame, as a pure function of the sharing state (so it can be
// tested without a server).
export const ShareDialogContents: React.FunctionComponent<{
    state: ISharingState;
    actions: IShareDialogActions;
    uiLanguage: string;
    now: Date;
    onClose: () => void;
}> = (props) => {
    const title = useL10n(
        "Share “%0”",
        "Sharing.ShareDialog.Title",
        undefined,
        props.state.collectionName,
    );
    const signedIn = !!props.state.signedInEmail;
    // Before the collection is shared, show the signed-in admin as its future sole admin, so the
    // list reads the same before and after the first invitation. (A Team Collection is already
    // shared by the time an admin sees this, because the server shares it, with everyone its
    // history shows working in it, the first time one of its admins asks for the state.)
    const members: ISharingMember[] =
        props.state.isShared || !signedIn || !props.state.canManage
            ? props.state.members
            : [
                  {
                      email: props.state.signedInEmail,
                      name: props.state.signedInName,
                      role: "admin",
                      invitedAt: props.now.toISOString(),
                      invitedBy: props.state.signedInEmail,
                      lastSeen: props.now.toISOString(),
                  },
              ];

    return (
        <>
            <DialogTitle title={title} preventCloseButton={true}>
                <LearnAboutSharingLink />
            </DialogTitle>
            <DialogMiddle
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 16px;
                    min-height: 200px;
                    overflow-x: hidden;
                `}
            >
                {!signedIn && <SignInPrompt signIn={props.actions.signIn} />}
                {signedIn && props.state.canManage && (
                    <InviteRow
                        members={members}
                        onInvite={(email, role) =>
                            props.actions.invite([{ email, role }])
                        }
                    />
                )}
                {signedIn && !props.state.canManage && (
                    <OnlyAdminsNote isShared={props.state.isShared} />
                )}
                {members.length > 0 && (
                    <div
                        data-testid="share-member-list"
                        css={css`
                            border-top: 1px solid ${kBannerGray};
                        `}
                    >
                        {members.map((member) => (
                            <MemberRow
                                key={member.email}
                                member={member}
                                isYou={sameEmail(
                                    member.email,
                                    props.state.signedInEmail,
                                )}
                                canManage={props.state.canManage}
                                uiLanguage={props.uiLanguage}
                                now={props.now}
                                actions={props.actions}
                            />
                        ))}
                    </div>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton onClick={props.onClose} default={true} />
            </DialogBottomButtons>
        </>
    );
};

// Where "Learn about sharing" goes. For now, the Team Collections introduction.
const kLearnAboutSharingUrl =
    "https://docs.bloomlibrary.org/team-collections-intro/";

// Top right of the title bar. BloomDialog routes clicks on http links to the system browser.
const LearnAboutSharingLink: React.FunctionComponent = () => {
    const label = useL10n(
        "Learn about sharing",
        "Sharing.ShareDialog.LearnAboutSharing",
    );
    return (
        <a
            href={kLearnAboutSharingUrl}
            data-testid="share-learn-link"
            css={css`
                margin-left: auto;
                display: flex;
                align-items: center;
                gap: 6px;
                color: ${kBloomBlue};
                text-decoration: none;
                white-space: nowrap;
                cursor: pointer;
                // DialogTitle makes everything in it 16px bold.
                && {
                    font-size: 14px;
                    font-weight: normal;
                }
                && * {
                    font-weight: normal;
                }
            `}
        >
            <HelpOutlineIcon fontSize="small" />
            <span>{label}</span>
        </a>
    );
};

const SignInPrompt: React.FunctionComponent<{ signIn: () => void }> = (
    props,
) => {
    const message = useL10n(
        "To share this collection, first sign in to BloomLibrary.org. The people you invite will use their own BloomLibrary.org accounts.",
        "Sharing.ShareDialog.SignInFirst",
    );
    return (
        <div
            data-testid="share-sign-in"
            css={css`
                display: flex;
                flex-direction: column;
                align-items: flex-start;
                gap: 12px;
            `}
        >
            <div>{message}</div>
            <BloomButton
                l10nKey="AccountMenu.SignIn"
                enabled={true}
                hasText={true}
                variant="contained"
                onClick={props.signIn}
            >
                Sign in
            </BloomButton>
        </div>
    );
};

const OnlyAdminsNote: React.FunctionComponent<{ isShared: boolean }> = (
    props,
) => {
    const sharedNote = useL10n(
        "Only admins can invite people or change roles.",
        "Sharing.ShareDialog.OnlyAdmins",
    );
    const notSharedNote = useL10n(
        "This collection is not shared yet. Only an administrator of this collection can share it.",
        "Sharing.ShareDialog.OnlyAdminsCanShare",
    );
    return (
        <div
            data-testid="share-only-admins"
            css={css`
                display: flex;
                align-items: center;
                gap: 12px;
                padding: 16px;
                border-radius: 4px;
                background-color: ${kFormBackground};
            `}
        >
            <InfoOutlinedIcon
                css={css`
                    color: ${kBloomBlue};
                `}
            />
            <span>{props.isShared ? sharedNote : notSharedNote}</span>
        </div>
    );
};

const InviteRow: React.FunctionComponent<{
    members: ISharingMember[];
    // Resolves to whether the invitation was made.
    onInvite: (email: string, role: SharingRole) => Promise<boolean>;
}> = (props) => {
    const [email, setEmail] = useState("");
    // True while an invitation is on its way, so it can't be sent twice.
    const [pending, setPending] = useState(false);
    const [role, setRoleValue] = useState<SharingRole>("editor");
    const [triedInvalid, setTriedInvalid] = useState(false);
    const emailLabel = useL10n(
        "Email address",
        "Sharing.ShareDialog.EmailAddress",
    );
    const invalidMessage = useL10n(
        "That does not look like an email address.",
        "Sharing.ShareDialog.InvalidEmail",
    );
    const alreadyMessage = useL10n(
        "That person already has access.",
        "Sharing.ShareDialog.AlreadyHasAccess",
    );
    const trimmed = email.trim();
    const alreadyHasAccess = props.members.some((m) =>
        sameEmail(m.email, trimmed),
    );
    let error = "";
    if (alreadyHasAccess) error = alreadyMessage;
    else if (triedInvalid && !isValidEmail(trimmed)) error = invalidMessage;

    const tryInvite = () => {
        if (alreadyHasAccess) return;
        if (!isValidEmail(trimmed)) {
            setTriedInvalid(true);
            return;
        }
        setPending(true);
        // Nothing is added to the list here: a successful invitation makes the server send
        // its sharing/stateChanged event, and useSharingState then fetches the new list.
        void props.onInvite(trimmed, role).then((invited) => {
            setPending(false);
            // Keep what was typed if it failed, so the admin can try again.
            if (invited) {
                setEmail("");
                setTriedInvalid(false);
            }
        });
    };

    return (
        <div
            data-testid="share-invite-row"
            css={css`
                display: flex;
                align-items: flex-start;
                gap: 12px;
            `}
        >
            <TextField
                data-testid="share-invite-email"
                size="small"
                placeholder={emailLabel}
                value={email}
                // Read-only while an invitation is on its way, since a successful one clears
                // the box and would otherwise wipe out a next address typed meanwhile.
                disabled={pending}
                error={!!error}
                helperText={error || undefined}
                autoFocus={true}
                onChange={(event) => setEmail(event.target.value)}
                onKeyDown={(event) => {
                    if (event.key === "Enter") {
                        event.preventDefault();
                        if (!pending) tryInvite();
                    }
                }}
                css={css`
                    flex-grow: 1;
                `}
            />
            <RoleMenu
                data-testid="share-invite-role"
                role={role}
                enabled={trimmed.length > 0}
                boxed={true}
                onChoose={setRoleValue}
            />
            <BloomButton
                data-testid="share-invite-button"
                l10nKey="Sharing.ShareDialog.Invite"
                enabled={trimmed.length > 0 && !alreadyHasAccess && !pending}
                hasText={true}
                variant="contained"
                onClick={tryInvite}
                css={css`
                    height: 40px;
                `}
            >
                Invite
            </BloomButton>
        </div>
    );
};

const PersonNameAndEmail: React.FunctionComponent<{
    name?: string;
    email: string;
    isYou?: boolean;
    faded?: boolean;
}> = (props) => {
    const you = useL10n("(you)", "Sharing.ShareDialog.You");
    const textColor = props.faded ? kMutedTextGray : "inherit";
    return (
        <div
            css={css`
                flex-grow: 1;
                min-width: 0;
                display: flex;
                flex-direction: column;
                color: ${textColor};
                overflow-wrap: anywhere;
            `}
        >
            <span>
                {props.name || props.email}
                {props.isYou && (
                    <span
                        css={css`
                            color: ${kMutedTextGray};
                        `}
                    >
                        {" " + you}
                    </span>
                )}
            </span>
            {props.name && (
                <span
                    css={css`
                        color: ${kMutedTextGray};
                        font-size: 0.9em;
                    `}
                >
                    {props.email}
                </span>
            )}
        </div>
    );
};

const RoleName: React.FunctionComponent<{ role: SharingRole }> = (props) => {
    const admin = useL10n("Admin", "Sharing.Role.Admin");
    const editor = useL10n("Editor", "Sharing.Role.Editor");
    return <span>{props.role === "admin" ? admin : editor}</span>;
};

const MemberRow: React.FunctionComponent<{
    member: ISharingMember;
    isYou: boolean;
    canManage: boolean;
    uiLanguage: string;
    now: Date;
    actions: IShareDialogActions;
}> = (props) => {
    // Everyone in the list has been invited; what we show is the last time we know they used
    // the collection, or, if we have no record of that, when they were given access. Someone
    // never seen is shown faded.
    const neverSeen = !props.member.lastSeen;
    const when = formatTimeAgo(
        props.member.lastSeen ?? props.member.invitedAt,
        props.now,
        props.uiLanguage,
    );
    const lastSeen = useL10n(
        "Last seen %0",
        "Sharing.ShareDialog.LastSeen",
        undefined,
        when,
    );
    const invitedWhen = useL10n(
        "Invited %0",
        "Sharing.ShareDialog.InvitedWhen",
        undefined,
        when,
    );
    const ownRoleTip = useL10n(
        "You can't change your own role. Another admin can do that for you.",
        "Sharing.ShareDialog.OwnRole",
    );
    return (
        <div
            data-testid="share-member"
            data-email={props.member.email}
            css={css`
                display: flex;
                align-items: center;
                gap: 16px;
                padding: 10px 0;
                border-bottom: 1px solid ${kBannerGray};
            `}
        >
            <div
                css={css`
                    opacity: ${neverSeen ? 0.6 : 1};
                `}
            >
                <BloomAvatar
                    email={props.member.email}
                    name={props.member.name || props.member.email}
                    avatarSizeInt={40}
                />
            </div>
            <PersonNameAndEmail
                name={props.member.name}
                email={props.member.email}
                isYou={props.isYou}
                faded={neverSeen}
            />
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    align-items: flex-end;
                    flex-shrink: 0;
                `}
            >
                {/* Nobody changes their own role or removes themselves, so an admin can only
                    step down once another admin has taken over. That also means a collection
                    can never be left without an admin. */}
                {props.canManage && !props.isYou ? (
                    <RoleMenu
                        data-testid="share-member-role"
                        role={props.member.role}
                        enabled={true}
                        onChoose={(role) => {
                            if (role !== props.member.role)
                                props.actions.setRole(props.member.email, role);
                        }}
                        onRemove={() =>
                            props.actions.remove(props.member.email)
                        }
                    />
                ) : (
                    <span
                        data-testid="share-member-fixed-role"
                        title={
                            props.canManage && props.isYou
                                ? ownRoleTip
                                : undefined
                        }
                    >
                        <RoleName role={props.member.role} />
                    </span>
                )}
                <span
                    data-testid="share-member-when"
                    css={css`
                        font-size: 0.9em;
                        color: ${neverSeen ? kBloomBlue : kMutedTextGray};
                        font-weight: ${neverSeen ? 500 : "normal"};
                    `}
                >
                    {neverSeen ? invitedWhen : lastSeen}
                </span>
            </div>
        </div>
    );
};

// A role, shown as a button that opens a menu of the roles (each with what it allows), plus
// optionally a Remove item.
const RoleMenu: React.FunctionComponent<{
    role: SharingRole;
    enabled: boolean;
    // The invite row's version has a box around it, to look like a field.
    boxed?: boolean;
    onChoose: (role: SharingRole) => void;
    onRemove?: () => void;
    "data-testid"?: string;
}> = (props) => {
    const [anchor, setAnchor] = useState<HTMLElement>();
    const adminDescription = useL10n(
        "Editor + collection settings and sharing",
        "Sharing.Role.Admin.Description",
    );
    const editorDescription = useL10n(
        "Add, remove, and edit books",
        "Sharing.Role.Editor.Description",
    );
    const removeLabel = useL10n(
        "Remove from collection",
        "Sharing.ShareDialog.RemoveFromCollection",
    );
    const close = () => setAnchor(undefined);
    const roleItem = (role: SharingRole, description: string) => {
        return (
            <MenuItem
                data-testid={`share-role-option-${role}`}
                selected={props.role === role}
                onClick={() => {
                    close();
                    props.onChoose(role);
                }}
                css={css`
                    align-items: flex-start;
                    white-space: normal;
                `}
            >
                <span
                    css={css`
                        width: 28px;
                        flex-shrink: 0;
                        color: ${kBloomBlue};
                    `}
                >
                    {props.role === role && <CheckIcon fontSize="small" />}
                </span>
                <div>
                    <div
                        css={css`
                            font-weight: 500;
                        `}
                    >
                        <RoleName role={role} />
                    </div>
                    <div
                        css={css`
                            font-size: 0.9em;
                            color: ${kMutedTextGray};
                        `}
                    >
                        {description}
                    </div>
                </div>
            </MenuItem>
        );
    };
    const DropIcon = anchor ? ArrowDropUpIcon : ArrowDropDownIcon;
    return (
        <>
            <Button
                data-testid={props["data-testid"]}
                disabled={!props.enabled}
                variant={props.boxed ? "outlined" : "text"}
                onClick={(event) => setAnchor(event.currentTarget)}
                endIcon={<DropIcon />}
                css={css`
                    text-transform: none;
                    color: ${anchor ? kBloomBlue : "inherit"};
                    font-size: inherit;
                    ${props.boxed
                        ? "height: 40px; border-radius: 0;"
                        : "padding: 0 0 0 8px; min-width: 0;"}
                    .MuiButton-endIcon {
                        margin-left: 0;
                        color: ${kBloomBlue};
                    }
                `}
            >
                <RoleName role={props.role} />
            </Button>
            <Menu
                anchorEl={anchor}
                open={!!anchor}
                onClose={close}
                anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                transformOrigin={{ vertical: "top", horizontal: "right" }}
                css={css`
                    .MuiMenu-paper {
                        max-width: 360px;
                    }
                `}
            >
                {roleItem("admin", adminDescription)}
                {roleItem("editor", editorDescription)}
                {props.onRemove && <Divider />}
                {props.onRemove && (
                    <MenuItem
                        data-testid="share-remove-member"
                        onClick={() => {
                            close();
                            props.onRemove?.();
                        }}
                        css={css`
                            color: ${kBloomRed};
                            padding-left: 44px;
                        `}
                    >
                        {removeLabel}
                    </MenuItem>
                )}
            </Menu>
        </>
    );
};
