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
    ConfigrCustomObjectInput,
    ConfigrBoolean,
    ConfigrSelect
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
import {
    postJson,
    useApiObject,
    useApiStringState
} from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";

let isOpenAlready = false;

type IPageStyle = { label: string; value: string };
type IPageStyles = Array<IPageStyle>;

export const BookSettingsDialog: React.FunctionComponent<{}> = () => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    const presetNames: IPageStyles = useApiObject<IPageStyles>(
        "book/settings/available-preset-names",
        []
    );

    const [settingsString, setSettingsString] = useApiStringState(
        "book/settings",
        "{}",
        () => propsForBloomDialog.open
    );

    const presetNames = useApiObject<Array<{ label: string; value: string }>>(
        "book/settings/available-preset-names",
        []
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
        <BloomDialog
            css={css`
                // TODO: we would like a background color, but setting it here makes the dialog's backdrop turn that color!
                // conceivably we could wrap the current childen in a div that just provides the background color.
                //background-color: #fbf8ff;
            `}
            // cssForDialogContents={css`
            //     background-color: #e4f1f3;
            //     height: 500px;
            // `}
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
                    // normally we want this: overflow-y: scroll;
                    overflow-y: hidden; // but I need help on the css, so we're going with this for now

                    // HACK: TODO get the divs to all just maximize height until the available space is used or we don't need anymore height
                    form {
                        overflow-y: scroll;
                        height: 600px;
                        width: 600px;
                        #groups {
                            margin-right: 10px; // make room for the scrollbar
                        }
                    }
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
                        showJson={false}
                        setValueOnRender={s => {
                            setSettingsToReturnLater(s);
                            //setSettings(s);
                        }}
                    >
                        <ConfigrGroup label="Appearance" level={1}>
                            <ConfigrSubgroup
                                label="Cover Background  (Not implemented yet)"
                                path={`appearance`}
                            >
                                <ConfigrCustomStringInput
                                    path={`appearance.coverColor`}
                                    disabled={true} //  We need more work to switch to allowing appearance CSS to control the book cover.
                                    //There is a work-in-progress branch called "CoverColorManager" that has my work on this.
                                    label="Cover Color"
                                    control={ColorPickerForConfigr}
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label="What to Show on Cover"
                                path={`appearance`}
                            >
                                <ConfigrBoolean
                                    path={`appearance.coverShowTitleL2`}
                                    label="Show Written Language 2 Title"
                                />
                                <ConfigrBoolean
                                    path={`appearance.coverShowTitleL3`}
                                    label="Show Written Language 3 Title"
                                />
                                <ConfigrBoolean
                                    path={`appearance.coverShowLanguageName`}
                                    label="Show Language Name"
                                />
                                <ConfigrBoolean
                                    path={`appearance.coverShowTopic`}
                                    label="Show Topic"
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label="Page Style"
                                path={`appearance`}
                            >
                                <ConfigrSelect
                                    label="Page Style Preset"
                                    path={`appearance.cssThemeName`}
                                    options={presetNames.map(x => {
                                        return {
                                            label: x.label.replace(
                                                "appearance-page-",
                                                ""
                                            ),
                                            value: x.value
                                        };
                                    })}
                                    description='Choose a "page style" to easily change margins, borders, an other page settings.'
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label="Front & Back Matter (Not implemented yet)"
                                path={`appearance`}
                            >
                                <ConfigrSelect
                                    disabled={true}
                                    label="Font & Back Matter"
                                    path={`appearance.TODO`}
                                    options={[
                                        { label: "Page Saver", value: "TODO" }
                                    ]}
                                    description={
                                        "Normally, books use the front & back matter pack that is chosen for the entire collection. Using this setting, you can cause this individual book to use a different one."
                                    }
                                />
                            </ConfigrSubgroup>
                        </ConfigrGroup>
                        <ConfigrGroup label="BloomPUB" level={1}>
                            <BloomResolutionSlider
                                label="Resolution"
                                path={`publish.bloomPUB.imageSettings`}
                            />
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
                    font-size: 12px;
                `}
                variant="h4"
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
        ShowEditViewDialog(<BookSettingsDialog />);
    }
}

const ColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
}> = props => {
    return (
        <ColorDisplayButton
            disabled={props.disabled}
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
