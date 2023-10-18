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
import {
    BloomPalette,
    getDefaultColorsFromPalette
} from "../../react_components/color-picking/bloomPalette";
import ColorPicker from "../../react_components/color-picking/colorPicker";
import {
    ColorDisplayButton,
    DialogResult
} from "../../react_components/color-picking/colorPickerDialog";
import { IColorInfo } from "../../react_components/color-picking/colorSwatch";
import { postJson, useApiStringState } from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";
import { useL10n } from "../../react_components/l10nHooks";
import { Div } from "../../react_components/l10nComponents";

let isOpenAlready = false;

export const BookSettingsDialog: React.FunctionComponent<{}> = () => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

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

    const bookSettingsTitle = useL10n("Book Settings", "BookSettings.Title");
    const bloomPubLabel = useL10n("BloomPUB", "BookSettings.BloomPub");
    const resolutionDescription = useL10n(
        "When images in books are really high resolution, it makes the books more difficult to view over poor internet connections. They will also take more space on phones. For this reason, Bloom reduces images to a maximum size.",
        "BookSettings.BloomPub.Resolution.Description"
    );
    const resolutionLabel = useL10n(
        "Resolution",
        "BookSettings.BloomPub.Resolution"
    );

    return (
        <BloomDialog
            css={css`
                background-color: #fbf8ff;
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
            <DialogTitle title={bookSettingsTitle} />
            <DialogMiddle
                css={css`
                    &:first-child {
                        margin-top: 0; // override the default that sees a lack of a title and adds a margin
                    }
                    overflow-y: auto; // This displays a scrollbar only when needed.  'scroll' always shows one.
                `}
            >
                {settings && (
                    <ConfigrPane
                        label={bookSettingsTitle}
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
                        showJson={false}
                        setValueOnRender={s => {
                            setSettingsToReturnLater(s);
                            //setSettings(s);
                        }}
                    >
                        {/* we'll bring this back later
                            <ConfigrGroup label="Appearance" level={1}>
                                <ConfigrCustomStringInput
                                    path={`appearance.coverColor`}
                                    label="Cover Color"
                                    control={ColorPickerForConfigr}
                                />
                            </ConfigrGroup> */}
                        <ConfigrGroup label={bloomPubLabel} level={1}>
                            <ConfigrSubgroup
                                label={resolutionLabel}
                                path={`publish.bloomPUB.imageSettings`}
                                description={resolutionDescription}
                            >
                                <BloomResolutionSlider
                                    path={`publish.bloomPUB.imageSettings`}
                                    label={resolutionLabel}
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
        <div>
            <ConfigrCustomObjectInput<Resolution>
                control={BloomResolutionSliderInner}
                {...props}
            ></ConfigrCustomObjectInput>
            <Div
                l10nKey="BookSettings.BloomPub.Resolution.Directions"
                css={css`
                    padding: 0 10px;
                    font-size: 9pt;
                `}
            >
                Set the maximum image resolution when making BloomPubs
                (including display on BloomLibrary)
            </Div>
        </div>
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
        { l: "4K", w: 3840, h: 2160 }
    ];
    let currentIndex = sizes.findIndex(x => x.w === props.value.maxWidth);
    if (currentIndex === -1) {
        currentIndex = 0;
    }
    const current = sizes[currentIndex];
    const currentLabel = useL10n(
        current.l,
        `BookSettings.BloomPub.Resolution.${current.l}`
    );

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                width: 200px; // todo: what should this be?
                padding: 0 10px; // allow space for tooltips
            `}
        >
            <Typography
                css={css`
                    text-align: right;
                    font-size: 12px;
                `}
                variant="h4"
            >{`${currentLabel}`}</Typography>
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
        ShowEditViewDialog(<BookSettingsDialog />);
    }
}

const ColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    onChange: (value: string) => void;
}> = props => {
    return (
        <ColorDisplayButton
            initialColor={props.value}
            localizedTitle={"foo"}
            transparency={false}
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
        />
    );
};
