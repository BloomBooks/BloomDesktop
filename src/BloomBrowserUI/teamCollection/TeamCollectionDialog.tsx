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
    IBloomDialogProps
} from "../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../react_components/BloomDialog/commonDialogComponents";
import { CollectionHistoryTable } from "./CollectionHistoryTable";
import { Tab, TabList, TabPanel, Tabs } from "react-tabs";
import { LocalizedString } from "../react_components/l10nComponents";
import { ThemeProvider } from "@material-ui/styles";
import { lightTheme } from "../bloomMaterialUITheme";
import "react-tabs/style/react-tabs.less";
import { useEffect, useState } from "react";
import { BloomTabs } from "../react_components/BloomTabs";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";

export const TeamCollectionDialogLauncher: React.FunctionComponent<{}> = props => {
    const {
        openingEvent,
        closeDialog,
        propsForBloomDialog
    } = useEventLaunchedBloomDialog("TeamCollectionDialog");

    return propsForBloomDialog.open ? (
        <TeamCollectionDialog
            closeDialog={closeDialog}
            propsForBloomDialog={propsForBloomDialog}
            showReloadButton={openingEvent.showReloadButton}
        />
    ) : null;
};

const TeamCollectionDialog: React.FunctionComponent<{
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
    // This ultimately controls which tab (currently Status or History) appears when the
    // dialog opens. The idea is that it shows the History tab unless there are important
    // messages in the Status tab.
    // The obvious solution is to use BloomApi.useApiBoolean to get the logImportant
    // value from the backend, and simply compute defaultTabIndex from that as either 1 or 0.
    // That doesn't work, because the defaultTabIndex only takes effect the FIRST time the
    // Tabs element is rendered. That will be with the default value passed to useApiBoolean.
    // The real value obtained in a later render after the backend responds is ignored.
    // To get around this (and reduce flicker), we don't render the Tabs element at all
    // until we have a real value for logImportant. The initial state, -1, indicates that
    // we're in this waiting state and shouldn't display the Tabs at all. When we get the
    // value from the API, we set defaultTabIndex to either 0 or 1 and the Tabs element is
    // rendered for the first time with the correct defaultIndex.
    const [defaultTabIndex, setDefaultTabIndex] = useState(1);

    useEffect(() => {
        BloomApi.getBoolean("teamCollection/logImportant", logImportant => {
            setDefaultTabIndex(logImportant ? 0 : 1);
        });
    }, []);
    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            // Note that we use these two props for width, but then we use css on the Tab itself for height.
            // It is important that the dialog not change size as you change tabs, which is why this
            // cannot be just responsive to the needs of the controls in the tabs.
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
                    <BloomTabs
                        defaultIndex={defaultTabIndex}
                        color="black"
                        selectedColor={kBloomBlue}
                        labelBackgroundColor="white"
                        css={css`
                            // see note above in the props of the <BloomDialog></BloomDialog>
                            height: 65vh;
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
                                    height: 350px;
                                    // enhance: there is a bug I haven't found where, if this is > 530px, then it overflows. Instead, the BloomDialog should keep growing.
                                    min-width: 530px;
                                `}
                            />
                        </TabPanel>
                        <TabPanel>
                            <CollectionHistoryTable />
                        </TabPanel>
                    </BloomTabs>
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
