import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPane } from "@sillsdev/config-r";
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
import { get, postJson } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";

export const CollectionSettingsDialog: React.FunctionComponent = () => {
    const {
        //openingEvent,
        closeDialog,
        propsForBloomDialog,
    } = useEventLaunchedBloomDialog("CollectionSettingsDialog");

    const [settings, setSettings] = React.useState<object | undefined>(
        undefined,
    );

    const [settingsString, setSettingsString] = React.useState<string>("{}");
    React.useEffect(() => {
        if (propsForBloomDialog.open)
            get("collection/settings", (result) => {
                setSettingsString(result.data);
            });
    }, [propsForBloomDialog.open]);

    const [settingsToReturnLater, setSettingsToReturnLater] =
        React.useState("");
    React.useEffect(() => {
        if (settingsString === "{}") {
            return; // leave settings as undefined
        }
        if (typeof settingsString === "string") {
            setSettings(JSON.parse(settingsString));
        } else {
            setSettings(settingsString);
        }
    }, [settingsString]);

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
            <DialogTitle title="Collection Settings"></DialogTitle>
            <DialogMiddle>
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        height: 100%;
                    `}
                >
                    <ConfigrPane
                        label={"Collection Settings"}
                        showSearch={true}
                        // showJson={true} // useful for debugging
                        initialValues={settings || {}}
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
                        <ConfigrGroup label={"Languages"}></ConfigrGroup>
                        <ConfigrGroup label={"Appearance"}></ConfigrGroup>
                    </ConfigrPane>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        postJson("collection/settings", settingsToReturnLater);
                        closeDialog();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
