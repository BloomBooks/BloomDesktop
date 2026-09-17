import * as React from "react";
import { css } from "@emotion/react";
import { ArrowDropDown } from "@mui/icons-material";
import Menu from "@mui/material/Menu";
import Divider from "@mui/material/Divider";
import { BloomTooltip } from "../../BloomToolTip";
import { TopRightMenuButton, topRightMenuArrowCss } from "./TopRightMenuButton";
import { useL10n } from "../../l10nHooks";
import { LocalizableMenuItem } from "../../localizableMenuItem";
import { callOnBlur } from "../../../utils/menuCloseOnBlur";
import { BloomAvatar } from "../../bloomAvatar";
import { useLoginState } from "../../useLoginState";
import { useApiObject } from "../../../utils/bloomApi";
import { RegistrationInfo } from "../../registration/registrationTypes";

// The account control shown at the top of the workspace's upper-right corner.
// When signed out, it is a simple "Sign in" button that launches the external
// browser login. When signed in, it shows the user's avatar; clicking it opens
// a menu with the signed-in email and a "Sign out" item.
export const AccountMenu: React.FunctionComponent = () => {
    const signInText = useL10n("Sign in", "AccountMenu.SignIn");
    const accountText = useL10n("Account", "AccountMenu.Account");

    const { email, signIn, signOut } = useLoginState();

    // Used to get a display name for the avatar. Fetched once; falls back gracefully
    // (BloomAvatar shows initials or a generated image) if it's empty.
    const registrationInfo = useApiObject<Partial<RegistrationInfo>>(
        "registration/userInfo",
        {},
    );
    const displayName = [registrationInfo.firstName, registrationInfo.surname]
        .filter((namePart) => !!namePart)
        .join(" ");

    const [anchorEl, setAnchorEl] = React.useState<HTMLElement>();

    const onClose = React.useCallback(() => {
        setAnchorEl(undefined);
    }, []);

    const onOpen = React.useCallback(() => {
        // This button is rendered by this component, so it exists when its own onClick fires.
        const button =
            document.getElementById("accountMenuButton") ?? undefined;
        setAnchorEl(button);
        callOnBlur(onClose);
    }, [onClose]);

    const handleSignOut = React.useCallback(() => {
        onClose();
        signOut();
    }, [onClose, signOut]);

    if (!email) {
        return (
            <TopRightMenuButton
                buttonId="accountMenuButton"
                text={signInText}
                onClick={signIn}
            />
        );
    }

    return (
        <React.Fragment>
            <BloomTooltip
                tip={{ l10nKey: "AccountMenu.Account", english: "Account" }}
            >
                <button
                    id="accountMenuButton"
                    aria-label={accountText}
                    onClick={onOpen}
                    css={css`
                        display: inline-flex;
                        align-items: center;
                        justify-content: end;
                        gap: 2px;
                        width: 100%;
                        background: transparent;
                        border: none;
                        padding: 0;
                        cursor: pointer;
                        color: inherit;
                    `}
                >
                    <BloomAvatar
                        email={email}
                        name={displayName}
                        avatarSizeInt={28}
                    />
                    <ArrowDropDown css={topRightMenuArrowCss} />
                </button>
            </BloomTooltip>
            <Menu
                open={Boolean(anchorEl)}
                anchorEl={anchorEl}
                onClose={onClose}
                disablePortal={false}
                keepMounted={false}
                anchorOrigin={{
                    vertical: "bottom",
                    horizontal: "left",
                }}
                transformOrigin={{
                    vertical: "top",
                    horizontal: "left",
                }}
                slotProps={{
                    paper: {
                        css: css`
                            min-width: 220px;
                        `,
                    },
                }}
            >
                <div
                    css={css`
                        padding: 6px 16px;
                        color: rgba(0, 0, 0, 0.6);
                        font-size: 0.875rem;
                    `}
                >
                    {email}
                </div>
                <Divider />
                <LocalizableMenuItem
                    english="Sign out"
                    l10nId="AccountMenu.SignOut"
                    onClick={handleSignOut}
                    hasLeadingIconSpace={false}
                />
            </Menu>
        </React.Fragment>
    );
};
