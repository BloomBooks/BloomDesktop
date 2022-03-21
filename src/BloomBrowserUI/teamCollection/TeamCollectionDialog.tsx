/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./TeamCollectionDialog.less";
import { useL10n } from "../react_components/l10nHooks";
import { ProgressBox } from "../react_components/Progress/progressBox";
import {
    IBloomWebSocketProgressEvent,
    useWebSocketListener
} from "../utils/WebSocketManager";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    IBloomDialogProps,
    useSetupBloomDialogFromServer
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../react_components/BloomDialog/commonDialogComponents";
import { CollectionHistoryTable } from "./CollectionHistoryTable";
import { Tab, TabList, TabPanel, Tabs } from "react-tabs";
import { LocalizedString } from "../react_components/l10nComponents";
import { ThemeProvider } from "@material-ui/styles";
import { lightTheme } from "../bloomMaterialUITheme";
import "react-tabs/style/react-tabs.less";

export const TeamCollectionDialog: React.FunctionComponent<{
    showReloadButtonForStorybook?: boolean;
    dialogEnvironmentForStorybook?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        params,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialogFromServer(
        "TeamCollectionDialog",
        props.dialogEnvironmentForStorybook
    );

    // We extract the core here so that we can avoid running most of the hook code when this dialog is not visible.
    return propsForBloomDialog.open ? (
        <TeamCollectionDialogInner
            closeDialog={closeDialog}
            propsForBloomDialog={propsForBloomDialog}
            showReloadButton={
                props.showReloadButtonForStorybook || params.showReloadButton
            }
        />
    ) : null;
};

const TeamCollectionDialogInner: React.FunctionComponent<{
    showReloadButton: boolean;
    closeDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
}> = props => {
    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );

    const events = BloomApi.useApiData<IBloomWebSocketProgressEvent[]>(
        "teamCollection/getLog",
        []
    );

    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            fullWidth={true}
            maxWidth="lg"
        >
            <ThemeProvider theme={lightTheme}>
                <DialogTitle
                    title={`${dialogTitle} (experimental)`}
                    icon={"Team Collection.svg"}
                    backgroundColor={kBloomBlue}
                    color={"white"}
                />
                <DialogMiddle>
                    <Tabs
                        defaultIndex={0}
                        // Seems like there should be some sort of Material-UI mode that would produce the look
                        // John wants. A lot of their examples are quite like it, but I can't find any reason
                        // why their examples are different from what I get with similar code. Possibly the
                        // makeStyles that is in most of their examples pulls in this look. But we're using Emotion.
                        css={css`
                            flex-grow: 1;
                            display: flex;
                            flex-direction: column;
                            height: 60vh;
                            .react-tabs__tab.react-tabs__tab {
                                background-color: white;
                                text-transform: uppercase;
                            }
                            .react-tabs__tab--selected {
                                color: ${kBloomBlue};
                                border-color: transparent;
                                border-bottom: 2px solid ${kBloomBlue};
                            }
                            .react-tabs__tab-list {
                                border: none;
                            }
                            .react-tabs__tab-panel--selected {
                                display: flex;
                                flex-direction: column;
                                flex-grow: 1;
                            }
                        `}
                    >
                        <TabList>
                            <Tab>
                                <LocalizedString
                                    l10nKey="TeamCollection.Status"
                                    l10nComment="Used as the name on a tab of the Team Collection dialog."
                                    temporarilyDisableI18nWarning={true}
                                >
                                    Status
                                </LocalizedString>
                            </Tab>
                            <Tab>
                                <LocalizedString
                                    l10nKey="TeamCollection.History"
                                    l10nComment="Used as the name on a tab of the Team Collection dialog."
                                    temporarilyDisableI18nWarning={true}
                                >
                                    History
                                </LocalizedString>
                            </Tab>
                        </TabList>
                        <TabPanel>
                            <ProgressBox
                                preloadedProgressEvents={events}
                                css={css`
                                    height: 350px;
                                    // enhance: there is a bug I haven't found where, if this is > 530px, then it overflows. Instead, the BloomDialog should keep growing.
                                    min-width: 530px;
                                `}
                            />
                        </TabPanel>
                        <TabPanel>
                            <CollectionHistoryTable />
                        </TabPanel>
                    </Tabs>
                </DialogMiddle>

                <DialogBottomButtons>
                    {props.showReloadButton && (
                        <DialogBottomLeftButtons>
                            <BloomButton
                                id="reload"
                                l10nKey="TeamCollection.Reload"
                                temporarilyDisableI18nWarning={true}
                                //variant="text"
                                enabled={true}
                                hasText={true}
                                onClick={() =>
                                    BloomApi.post("common/reloadCollection")
                                }
                            >
                                Reload Collection
                            </BloomButton>
                        </DialogBottomLeftButtons>
                    )}
                    <DialogCloseButton
                        onClick={props.closeDialog}
                        // default action is to close *unless* we're showing reload
                        default={!props.showReloadButton}
                    />
                </DialogBottomButtons>
            </ThemeProvider>
        </BloomDialog>
    );
};
