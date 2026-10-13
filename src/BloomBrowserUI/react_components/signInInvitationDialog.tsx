import { css } from "@emotion/react";

import * as React from "react";
import { useCallback, useEffect, useState } from "react";
import HelpOutlineIcon from "@mui/icons-material/HelpOutline";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "./BloomDialog/BloomDialog";
import BloomButton from "./bloomButton";
import { post, postString, useApiObject } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { Div } from "./l10nComponents";
import { Link } from "./link";
import { useLoginState } from "./useLoginState";

// Where "Learn about Bloom Collaboration" takes the user. This is the closest page we have today;
// point it at a page about Bloom Collaboration itself once one is written.
const kCollaborationDocsUrl = "https://docs.bloomlibrary.org/team-collections";

// Asks the server, as the collection tab mounts, whether the user should be invited to sign in --
// which is the case when we have just opened a Team Collection and nobody is signed in to
// BloomLibrary.org. See AccountApi.HandleSignInInvitationNeeded, which explains why we ask rather
// than having C# push the dialog at us during startup.
export const SignInInvitationDialogLauncher: React.FunctionComponent = () => {
    const invitation = useApiObject<{ needed: boolean }>(
        "account/signInInvitationNeeded",
        { needed: false },
    );
    // Once we have shown the dialog, the user closing it must stick, so we cannot key "open" off
    // the server's answer.
    const [closed, setClosed] = useState(false);
    const closeDialog = useCallback(() => setClosed(true), []);
    const showing = invitation.needed && !closed;

    // Tell the server the invitation has been delivered, so this run is done inviting. We report it
    // now rather than letting the question itself count, so that a page reload between asking and
    // showing doesn't swallow the invitation.
    useEffect(() => {
        if (showing) post("account/signInInvitationShown");
    }, [showing]);

    return showing ? (
        <SignInInvitationDialog closeDialog={closeDialog} />
    ) : null;
};

// Invites a Team Collection user to sign in to BloomLibrary.org, because team collaboration is
// moving to the web and an account is what will get them ready for it (BL-16692).
export const SignInInvitationDialog: React.FunctionComponent<{
    closeDialog: () => void;
}> = (props) => {
    const dialogTitle = useL10n(
        "Please sign in to Bloom",
        "SignInInvitationDialog.Title",
    );
    const { email, signIn } = useLoginState();
    const closeDialog = props.closeDialog;

    // Signing in happens out in the user's browser, which reports back to Bloom when it succeeds.
    // Once that happens there is nothing left to invite the user to do, so get out of their way.
    useEffect(() => {
        if (email) closeDialog();
    }, [email, closeDialog]);

    return (
        <BloomDialog open={true} onClose={closeDialog}>
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    width: 400px;
                `}
            >
                <Div l10nKey="SignInInvitationDialog.Message">
                    We're simplifying how teams work together in Bloom. Nothing
                    changes for your collection yet, but signing in now will get
                    you ready.
                </Div>
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        margin-top: 20px;
                        // The icon is a bit taller than the link text; keep them optically aligned.
                        svg {
                            font-size: 20px;
                            margin-right: 8px;
                        }
                    `}
                >
                    <HelpOutlineIcon color="primary" />
                    <Link
                        l10nKey="SignInInvitationDialog.LearnAboutCollaboration"
                        onClick={() =>
                            postString("link", kCollaborationDocsUrl)
                        }
                    >
                        Learn about Bloom Collaboration
                    </Link>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        id="notAbleToSignInYet"
                        variant="text"
                        enabled={true}
                        l10nKey="SignInInvitationDialog.NotAbleYet"
                        hasText={true}
                        onClick={props.closeDialog}
                    >
                        I'm not able to yet
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    id="signInToBloomLibrary"
                    variant="contained"
                    enabled={true}
                    l10nKey="SignInInvitationDialog.SignIn"
                    hasText={true}
                    size="medium"
                    // We deliberately leave the dialog open: signing in happens in the user's
                    // browser, and the effect above closes us when that succeeds. If they give up
                    // there, they can still say they aren't able to yet.
                    onClick={signIn}
                >
                    Sign in to BloomLibrary.org
                </BloomButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};
