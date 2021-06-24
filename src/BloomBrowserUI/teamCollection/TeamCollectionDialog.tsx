/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./TeamCollectionDialog.less";
import { useL10n } from "../react_components/l10nHooks";
import { ProgressBox } from "../react_components/Progress/progressBox";
import { IBloomWebSocketProgressEvent } from "../utils/WebSocketManager";
import { kBloomBlue } from "../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../react_components/BloomDialog/commonDialogComponents";
import { CollectionHistoryTable } from "./CollectionHistoryTable";
import { Tab, TabList, TabPanel, Tabs } from "react-tabs";
import { LocalizedString } from "../react_components/l10nComponents";
import { ThemeProvider } from "@material-ui/styles";
import theme from "../bloomMaterialUITheme";
import { WireUpForWinforms } from "../utils/WireUpWinform";
export let showTeamCollectionDialog: () => void;
import "react-tabs/style/react-tabs.less";

export const TeamCollectionDialog: React.FunctionComponent<{
    showReloadButton: boolean;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    // hoist this up to the window level so that any code that imports showTeamCollectionDialog can show it
    // (It will still have to be declared once at the app level when it is no longer launched in its own winforms dialog.)
    showTeamCollectionDialog = showDialog;

    const dialogTitle = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection"
    );

    const events = BloomApi.useApiData<IBloomWebSocketProgressEvent[]>(
        "teamCollection/getLog",
        []
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <ThemeProvider theme={theme}>
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
                                    // If we have omitOuterFrame that means the dialog height is controlled by c#, so let the progress grow to fit it.
                                    // Maybe we could have that approach *all* the time?
                                    height: ${props.dialogEnvironment
                                        ?.dialogFrameProvidedExternally
                                        ? "100%"
                                        : "350px"};
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
                        onClick={closeDialog}
                        // default action is to close *unless* we're showing reload
                        default={!props.showReloadButton}
                    />
                </DialogBottomButtons>
            </ThemeProvider>
        </BloomDialog>
    );
};

WireUpForWinforms(TeamCollectionDialog);
