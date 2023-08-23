import React = require("react");

import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog/BloomDialog";
import BloomButton from "./bloomButton";
import { post, postData, postString } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { Div, Label } from "./l10nComponents";
import { DialogCloseButton } from "./BloomDialog/commonDialogComponents";
import { css } from "@emotion/react";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "./BloomDialog/BloomDialogPlumbing";
import { WireUpForWinforms } from "../utils/WireUpWinform";

export const ForumInvitationDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment
    );
    const dialogTitle = useL10n(
        "Bloom Community Forum",
        "ForumInvitationDialog.BloomCommunityForum"
    );
    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    height: 170px;
                    width: 400px;
                `}
            >
                <Div l10nKey="ForumInvitationDialog.DoYouKnow">
                    Do you know about our Bloom forum? Join us to:
                </Div>
                <ul>
                    <li>
                        <Label l10nKey="ForumInvitationDialog.LearnAboutNewFeatures">
                            learn about new features
                        </Label>
                    </li>
                    <li>
                        <Label l10nKey="ForumInvitationDialog.RequestAndVote">
                            request and vote on new features
                        </Label>
                    </li>
                    <li>
                        <Label l10nKey="ForumInvitationDialog.AskForAdvice">
                            ask for advice or technical help
                        </Label>
                    </li>
                    <li>
                        <Label l10nKey="ForumInvitationDialog.ShowOff">
                            show off what you're doing with Bloom!
                        </Label>
                    </li>
                </ul>
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    l10nKey={"ForumInvitationDialog.HowToJoin"}
                    enabled={true}
                    onClick={() => {
                        // Stop bringing back up the forum invitation once user has clicked "How to Join"
                        postData("app/UserSetting", {
                            settingName: "ForumInvitationAcknowledged",
                            settingValue: "true"
                        });
                        postString(
                            "link",
                            "https://docs.bloomlibrary.org/forum"
                        );
                        post("common/closeReactDialog");
                    }}
                    hasText={true}
                >
                    How to Join
                </BloomButton>

                <DialogCloseButton
                    onClick={() => post("common/closeReactDialog")}
                    css={css`
                        background-color: white;
                        color: ${kBloomBlue} !important;
                        border: 1px solid ${kBloomBlue} !important;
                    `}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(ForumInvitationDialog);
