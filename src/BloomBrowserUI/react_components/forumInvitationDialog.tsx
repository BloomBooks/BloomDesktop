import React = require("react");
import * as ReactDOM from "react-dom";

import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogProps
} from "./BloomDialog/BloomDialog";
import BloomButton from "./bloomButton";
import { useEventLaunchedBloomDialog } from "./BloomDialog/BloomDialogPlumbing";
import { useEffect } from "react";
import { get, postData, postString } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { Div, Label } from "./l10nComponents";
import { DialogCloseButton } from "./BloomDialog/commonDialogComponents";
import { css } from "@emotion/react";
import { kBloomBlue } from "../bloomMaterialUITheme";

export const ForumInvitationDialogLauncher: React.FunctionComponent<{}> = () => {
    const {
        openingEvent,
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useEventLaunchedBloomDialog("ForumInvitationDialog");

    useEffect(() => {
        get(
            "app/UserSettings?settingName=ForumInvitationAcknowledged",
            result => {
                if (result.data.settingValue) {
                    // forum invitation has been acknowledged, don't show dialog
                    return;
                }

                get(
                    "app/UserSettings?settingName=ForumInvitationLastShown",
                    result => {
                        const lastShownDate = new Date(
                            result.data.settingValue
                        );
                        const today = new Date();
                        const diff = today.getTime() - lastShownDate.getTime();
                        const diffDays = Math.floor(diff / (1000 * 3600 * 24));
                        if (diffDays > 13) {
                            //show once every two weeks
                            showDialog();
                            postData("app/UserSettings", {
                                settingName: "ForumInvitationLastShown",
                                settingValue: today.toISOString()
                            });
                        }
                    }
                );
            }
        );
    }, []);

    return propsForBloomDialog.open ? (
        <ForumInvitationDialog
            closeDialog={closeDialog}
            showDialog={showDialog}
            propsForBloomDialog={propsForBloomDialog}
        />
    ) : null;
};

export const ForumInvitationDialog: React.FunctionComponent<{
    closeDialog: () => void;
    showDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
}> = props => {
    const dialogTitle = useL10n(
        "Bloom Community Forum",
        "ForumInvitationDialog.BloomCommunityForum"
    );
    return (
        <BloomDialog {...props.propsForBloomDialog}>
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    height: 170px;
                    width: 400px;
                    position: sticky;
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
                        postData("app/UserSettings", {
                            settingName: "ForumInvitationAcknowledged",
                            settingValue: "true"
                        });
                        postString(
                            "link",
                            "https://docs.bloomlibrary.org/forum"
                        );
                        props.closeDialog();
                    }}
                    hasText={true}
                >
                    HOW TO JOIN
                </BloomButton>

                <DialogCloseButton
                    onClick={props.closeDialog}
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
