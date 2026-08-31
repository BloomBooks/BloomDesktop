import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrValues,
    ConfigrGroup,
    ConfigrPage,
    ConfigrPane,
    ConfigrStatic,
} from "@sillsdev/config-r";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { get, postJson } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";

export const CollectionSettingsDialog: React.FunctionComponent = () => {
    const {
        //openingEvent,
        closeDialog,
        propsForBloomDialog,
    } = useEventLaunchedBloomDialog("CollectionSettingsDialog");

    const [settingsString, setSettingsString] = React.useState<string>("{}");
    // Fetch collection settings when the dialog opens so we sync with host state.
    React.useEffect(() => {
        if (propsForBloomDialog.open)
            get("collection/settings", (result) => {
                setSettingsString(result.data);
            });
    }, [propsForBloomDialog.open]);

    const settings = React.useMemo((): ConfigrValues | undefined => {
        if (settingsString === "{}") {
            return undefined;
        }
        if (typeof settingsString === "string") {
            return JSON.parse(settingsString) as ConfigrValues;
        }
        return settingsString as unknown as ConfigrValues;
    }, [settingsString]);

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        ConfigrValues | undefined
    >(undefined);
    const dialogTitle = useL10n(
        "Collection Settings",
        "CollectionSettingsDialog.Title",
    );

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
            onCancel={() => {
                closeDialog();
            }}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle}></DialogTitle>
            <DialogMiddle>
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        height: 100%;
                    `}
                >
                    {settings && (
                        <ConfigrPane
                            label={dialogTitle}
                            showAppBar={false}
                            showSearch={true}
                            // showJson={true} // useful for debugging
                            initialValues={settings}
                            //themeOverrides={lightTheme}
                            themeOverrides={{
                                // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                                palette: {
                                    primary: { main: kBloomBlue },
                                },
                            }}
                            onChange={(s) => {
                                setSettingsToReturnLater(s);
                            }}
                        >
                            <ConfigrPage
                                label={"Languages"}
                                pageKey="languages"
                                topLevel={true}
                            >
                                <ConfigrGroup label={"Languages"}>
                                    <ConfigrStatic>
                                        <div
                                            css={css`
                                                font-size: 0.9em;
                                                color: #555;
                                            `}
                                        >
                                            Settings for this section are not
                                            available yet.
                                        </div>
                                    </ConfigrStatic>
                                </ConfigrGroup>
                            </ConfigrPage>
                            <ConfigrPage
                                label={"Appearance"}
                                pageKey="appearance"
                                topLevel={true}
                            >
                                <ConfigrGroup label={"Appearance"}>
                                    <ConfigrStatic>
                                        <div
                                            css={css`
                                                font-size: 0.9em;
                                                color: #555;
                                            `}
                                        >
                                            Settings for this section are not
                                            available yet.
                                        </div>
                                    </ConfigrStatic>
                                </ConfigrGroup>
                            </ConfigrPage>
                        </ConfigrPane>
                    )}
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        if (settingsToReturnLater) {
                            postJson(
                                "collection/settings",
                                settingsToReturnLater,
                            );
                        }
                        closeDialog();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
