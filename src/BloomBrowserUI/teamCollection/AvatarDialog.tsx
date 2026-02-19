import { css } from "@emotion/react";

import * as React from "react";
import { useL10n } from "../react_components/l10nHooks";
import { Div } from "../react_components/l10nComponents";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { TextWithEmbeddedLink } from "../react_components/link";
import BloomButton from "../react_components/bloomButton";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { showRegistrationDialog } from "../react_components/registration/registrationDialog";

// Dialog shown (when props.open is true) in response to the "About my Avatar..." menu item
// in the TeamCollectionBookStatusPanel.
export const AvatarDialog: React.FunctionComponent<{
    open: boolean;
    close: () => void;
    currentUser: string;
    currentUserName: string;
}> = (props) => {
    const title = useL10n(
        "Your Team Collection Avatar & Name",
        "TeamCollection.AvatarAndName",
        undefined,
        undefined,
        undefined,
        true,
    );
    const avatar = (
        <BloomAvatar email={props.currentUser} name={props.currentUserName} />
    );
    return (
        <BloomDialog open={props.open} onClose={props.close}>
            <DialogTitle title={title}></DialogTitle>
            <DialogMiddle
                css={css`
                    margin-top: 10px;
                    margin-bottom: 10px;
                `}
            >
                <Div
                    l10nKey="TeamCollection.AvatarName"
                    temporarilyDisableI18nWarning={true}
                >
                    The following avatar and name appear on books that you check
                    out:
                </Div>
                <div
                    css={css`
                        display: flex;
                        margin-top: 10px;
                        margin-bottom: 10px;
                        align-items: center;
                    `}
                >
                    {avatar}
                    <div
                        css={css`
                            margin-left: 15px;
                        `}
                    >
                        {props.currentUserName}
                    </div>
                </div>
                <TextWithEmbeddedLink
                    l10nKey="TeamCollection.HeadToGravatar"
                    href="https://Gravatar.com"
                    temporarilyDisableI18nWarning={true}
                >
                    If you don't see a picture, head on over to [Gravatar.com]
                    and upload one to go with your email address. You can also
                    update your name & email address in your Bloom registration.
                </TextWithEmbeddedLink>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        id="registration"
                        variant="text"
                        enabled={true}
                        l10nKey="Common.Registration"
                        temporarilyDisableI18nWarning={true}
                        onClick={() => {
                            props.close();
                            showRegistrationDialog({});
                        }}
                        hasText={true}
                    >
                        Bloom Registration
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    enabled={true}
                    variant="contained"
                    l10nKey="Common.Close"
                    hasText={true}
                    size="medium"
                    onClick={props.close}
                >
                    Close
                </BloomButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};
