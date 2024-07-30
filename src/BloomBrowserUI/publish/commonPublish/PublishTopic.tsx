/** @jsx jsx **/
import { css, jsx } from "@emotion/react";
import * as React from "react";

import { IconButton } from "@mui/material";
import EditIcon from "@mui/icons-material/Edit";
import ErrorIcon from "@mui/icons-material/Error";

import { useL10n } from "../../react_components/l10nHooks";
import { get } from "../../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../../utils/WebSocketManager";
import { Mode } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { SettingsGroup } from "./PublishScreenBaseComponents";
import {
    showTopicChooserDialog,
    TopicChooserDialog
} from "../../bookEdit/TopicChooser/TopicChooserDialog";

export const PublishTopic: React.FunctionComponent<{}> = () => {
    const [topicName, setTopicName] = React.useState("");

    function retrieveTopic() {
        get("libraryPublish/topic", result => {
            setTopicName(result.data);
        });
    }
    React.useEffect(() => {
        retrieveTopic();
    });
    useSubscribeToWebSocketForEvent("publish", "topicChanged", retrieveTopic);

    return (
        <React.Fragment>
            <SettingsGroup label={useL10n("Topic", "Topic")}>
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                        justify-content: space-between;
                        align-items: center;
                    `}
                >
                    {topicName === "Missing" ? (
                        <span
                            css={css`
                                color: red;
                                text-wrap: nowrap;
                            `}
                        >
                            <ErrorIcon
                                css={css`
                                    vertical-align: bottom;
                                    margin-right: 5px;
                                `}
                            />
                            Missing
                        </span>
                    ) : (
                        <span>{topicName}</span>
                    )}
                    <IconButton
                        color="primary"
                        onClick={() => showTopicChooserDialog(Mode.Publish)}
                    >
                        <EditIcon />
                    </IconButton>
                </div>
            </SettingsGroup>
            <TopicChooserDialog />
        </React.Fragment>
    );
};
