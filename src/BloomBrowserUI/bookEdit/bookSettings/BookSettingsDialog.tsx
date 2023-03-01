import { css } from "@emotion/react";
import {
    ConfigrPane,
    ConfigrGroup,
    ConfigrSubgroup,
    ConfigrCustomStringInput,
    ConfigrColorPicker,
    ConfigrInput,
    ConfigrBoolean
} from "@sillsdev/config-r";
import React = require("react");
import {
    kBloomBlue,
    kUiFontStack,
    lightTheme
} from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogCloseButton,
    DialogOkButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import { postJson, useApiStringState } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";

let isOpenAlready = false;

export const BookSettingsDialog: React.FunctionComponent<{
    data: any;
}> = () => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    // const [settings, setSettings] = useApiStringState(
    //     "book/settings",
    //     "{}",
    //     () => propsForBloomDialog.open
    // );

    const [settingsString, setSettingsString] = useApiStringState(
        "book/settings",
        "{}",
        () => propsForBloomDialog.open
    );
    const [settings, setSettings] = React.useState<object | undefined>(
        undefined
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
        <React.Fragment>
            <BloomDialog
                cssForDialogContents={css`
                    background-color: white;
                `}
                {...propsForBloomDialog}
                onClose={closeDialog}
                onCancel={() => {
                    isOpenAlready = false;
                    closeDialog();
                }}
                draggable={false}
                maxWidth={false}
            >
                <DialogTitle title="Book Settings" />
                <DialogMiddle
                    css={css`
                        &:first-child {
                            margin-top: 0; // override the default that sees a lack of a title and adds a margin
                        }
                        overflow-y: scroll;
                    `}
                >
                    {settings && (
                        <ConfigrPane
                            label="Book Settings"
                            initialValues={settings}
                            showAllGroups={true}
                            //themeOverrides={lightTheme}
                            themeOverrides={{
                                // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                                palette: {
                                    primary: { main: kBloomBlue }
                                }
                            }}
                            showAppBar={false}
                            showJson={true}
                            setValueOnRender={setSettingsToReturnLater}
                        >
                            <ConfigrGroup label="Appearance" level={1}>
                                <ConfigrSubgroup
                                    label="Cover"
                                    path={`appearance.cover`}
                                >
                                    <ConfigrCustomStringInput
                                        path={`appearance.cover.coverColor`}
                                        label="Cover Color"
                                        control={ConfigrColorPicker}
                                    />
                                </ConfigrSubgroup>
                            </ConfigrGroup>
                            <ConfigrGroup label="BloomPUB" level={1}>
                                <ConfigrSubgroup
                                    label="Resolution"
                                    path={`publish.bloomPUB.imageSettings`}
                                    description={
                                        "When images in books are really high resolution, it makes the books more difficult to view over poor internet connections. They will also take more space on phones. For this reason, Bloom reduces images to a maximum size."
                                    }
                                >
                                    <ConfigrInput
                                        path={`publish.bloomPUB.imageSettings.maxWidth`}
                                        label="Maximum Width"
                                        type="number"
                                        units="pixels"
                                    />
                                    <ConfigrInput
                                        path={`publish.bloomPUB.imageSettings.maxHeight`}
                                        label="Maximum Height"
                                        type="number"
                                        units="pixels"
                                    />
                                </ConfigrSubgroup>
                            </ConfigrGroup>
                        </ConfigrPane>
                    )}
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogOkButton
                        onClick={() => {
                            postJson("book/settings", settingsToReturnLater);
                            isOpenAlready = false;
                            closeDialog();
                            // todo: how do we make the pageThumbnailList reload? It's in a different browser, so
                            // we can't use a global. It listens to websocket, but we currently can only listen,
                            // we cannot send.
                        }}
                    />
                    <DialogCancelButton />
                </DialogBottomButtons>
            </BloomDialog>
        </React.Fragment>
    );
};

export function showBookSettingsDialog() {
    // once Bloom's tab bar is also in react, it won't be possible
    // to open another copy of this without closing it first, but
    // for now, we need to prevent that.
    if (!isOpenAlready) {
        isOpenAlready = true;
        ShowEditViewDialog(<BookSettingsDialog data={{}} />);
    }
}
