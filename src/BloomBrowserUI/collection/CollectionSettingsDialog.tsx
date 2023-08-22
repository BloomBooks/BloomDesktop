/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPane } from "@sillsdev/config-r";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogProps
} from "../react_components/BloomDialog/BloomDialog";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton
} from "../react_components/BloomDialog/commonDialogComponents";
import { useApiStringState } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";

export const CollectionSettingsDialogLauncher: React.FunctionComponent<{}> = () => {
    const {
        //openingEvent,
        closeDialog,
        propsForBloomDialog
    } = useEventLaunchedBloomDialog("CollectionSettingsDialog");

    // We extract the core here so that we can avoid running most of the hook code when this dialog is not visible.
    return propsForBloomDialog.open ? (
        <CollectionSettingsDialog
            closeDialog={closeDialog}
            propsForBloomDialog={propsForBloomDialog}
        />
    ) : null;
};

const CollectionSettingsDialog: React.FunctionComponent<{
    closeDialog: () => void;
    propsForBloomDialog: IBloomDialogProps;
}> = props => {
    const [settings, setSettings] = React.useState<object | undefined>(
        //undefined
        { a: 1, b: 2 } // gibberish to make the dialog come up
    );

    const [settingsString, _] = useApiStringState(
        "collection/settings",
        "{}",
        () => props.propsForBloomDialog.open
    );
    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState(
        ""
    );
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
            {...props.propsForBloomDialog}
            onClose={props.closeDialog}
            onCancel={() => {
                props.closeDialog();
            }}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title="Collection Settings"></DialogTitle>
            <DialogMiddle>
                {settings && (
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
                            initialValues={settings}
                            showAllGroups={true}
                            //themeOverrides={lightTheme}
                            themeOverrides={{
                                // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                                palette: {
                                    primary: { main: kBloomBlue }
                                }
                            }}
                            setValueOnRender={s => {
                                setSettingsToReturnLater(s);
                            }}
                        >
                            <ConfigrGroup label={"Languages"}></ConfigrGroup>
                            <ConfigrGroup label={"Appearance"}></ConfigrGroup>
                        </ConfigrPane>
                    </div>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        //postJson("collection/settings", settingsToReturnLater);
                        props.closeDialog();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
