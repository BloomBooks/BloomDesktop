import { css } from "@emotion/react";
import { Slider, Typography } from "@mui/material";
import {
    ConfigrPane,
    ConfigrGroup,
    ConfigrSubgroup,
    ConfigrCustomStringInput,
    ConfigrCustomNumberInput,
    ConfigrColorPicker,
    ConfigrInput,
    ConfigrCustomObjectInput
} from "@sillsdev/config-r";
import React = require("react");
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
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
                            setValueOnRender={s => {
                                setSettingsToReturnLater(s);
                                //setSettings(s);
                            }}
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
                                    <BloomResolutionSlider
                                        path={`publish.bloomPUB.imageSettings`}
                                        label="Resolution"
                                    />
                                </ConfigrSubgroup>
                            </ConfigrGroup>
                        </ConfigrPane>
                    )}
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogOkButton
                        default={true}
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

type Resolution = {
    maxWidth: number;
    maxHeight: number;
};

const BloomResolutionSlider: React.FunctionComponent<React.PropsWithChildren<{
    path: string;
    label: string;
}>> = props => {
    return (
        <ConfigrCustomObjectInput<Resolution>
            control={BloomResolutionSliderInner}
            {...props}
        ></ConfigrCustomObjectInput>
    );
};

const BloomResolutionSliderInner: React.FunctionComponent<{
    value: Resolution;
    onChange: (value: Resolution) => void;
}> = props => {
    const sizes = [
        { l: "Small", w: 600, h: 600 },
        { l: "HD", w: 1280, h: 720 },
        { l: "Full HD", w: 1920, h: 1080 },
        { l: "4k", w: 3840, h: 2160 }
    ];
    let currentIndex = sizes.findIndex(x => x.w === props.value.maxWidth);
    if (currentIndex === -1) {
        currentIndex = 0;
    }
    const current = sizes[currentIndex];

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                width: 200px; // todo: what should this be?
            `}
        >
            <Typography
                css={css`
                    text-align: right;
                `}
                variant="h3"
            >{`${current.l}`}</Typography>
            <Slider
                track={false}
                max={sizes.length - 1}
                min={0}
                step={1}
                value={currentIndex}
                valueLabelFormat={() => {
                    return `${current.w}x${current.h}`;
                }}
                onChange={(e, value) => {
                    props.onChange({
                        maxWidth: sizes[value as number].w,
                        maxHeight: sizes[value as number].h
                    });
                }}
                valueLabelDisplay="auto"
            ></Slider>
        </div>
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