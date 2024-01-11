import { css } from "@emotion/react";
import { ListItem, Slider, Typography } from "@mui/material";
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
    post,
    postJson,
    useApiObject,
    useApiStringState
} from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";
import { useL10n } from "../../react_components/l10nHooks";
import { Div } from "../../react_components/l10nComponents";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { default as TrashIcon } from "@mui/icons-material/Delete";

let isOpenAlready = false;

type IPageStyle = { label: string; value: string };
type IPageStyles = Array<IPageStyle>;
type IAppearanceUIOptions = {
    firstPossiblyLegacyCss?: string;
    themeNames: IPageStyles;
};

// Stuff we find in the appearance property of the object we get from the book/settings api.
// Not yet complete
export interface IAppearanceSettings {
    cssThemeName: string;
}

// Stuff we get from the book/settings api.
// Not yet complete
export interface IBookSettings {
    appearance: IAppearanceSettings;
    firstPossiblyLegacyCss?: string;
}

export const BookSettingsDialog: React.FunctionComponent<{}> = () => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    const appearanceUIOptions: IAppearanceUIOptions = useApiObject<
        IAppearanceUIOptions
    >("book/settings/appearanceUIOptions", {
        themeNames: []
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

    const [appearanceDisabled, setAppearanceDisabled] = React.useState(false);

    // We use state here to allow the dialog UI to update without permanently changing the settings
    // and getting notified of  those changes. The changes are persisted when the user clicks OK
    // (except for the button to delete customBookStyles.css, which is done immediately).
    // A downside of this is that when we delete customBookStyles.css, we don't know whether
    // the result will be no conflicts or that customCollectionStyles.css will now be the
    // firstPossiblyLegacyCss. For now it just behaves as if there are now no conflicts.
    // One possible approach is to have the server return the new firstPossiblyLegacyCss
    // as the result of the deleteCustomBookStyles call.
    const [theme, setTheme] = React.useState("");
    const [firstPossiblyLegacyCss, setFirstPossiblyLegacyCss] = React.useState(
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

    React.useEffect(() => {
        setFirstPossiblyLegacyCss(
            appearanceUIOptions?.firstPossiblyLegacyCss ?? ""
        );
    }, [appearanceUIOptions]);

    const bookSettingsTitle = useL10n("Book Settings", "BookSettings.Title");
    React.useEffect(() => {
        if (settings && (settings as any).appearance) {
            const liveSettings = settingsToReturnLater || settings;
            setAppearanceDisabled(
                (liveSettings as any)?.appearance?.cssThemeName === "legacy-5-6"
            );
            setTheme((liveSettings as IBookSettings)?.appearance?.cssThemeName);
        }
    }, [settings, settingsToReturnLater]);

    const deleteCustomBookStyles = () => {
        post("book/settings/deleteCustomBookStyles");
        setFirstPossiblyLegacyCss("");
    };

    return (
        <BloomDialog
            css={css`
                // TODO: we would like a background color, but setting it here makes the dialog's backdrop turn that color!
                // conceivably we could wrap the current children in a div that just provides the background color.
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
            <DialogTitle title={bookSettingsTitle} />
            <DialogMiddle
                css={css`
                    &:first-child {
                        margin-top: 0; // override the default that sees a lack of a title and adds a margin
                    }
                    overflow-y: auto; // This displays a scrollbar only when needed.  'scroll' always shows one.
                    // hack might need this: overflow-y: hidden; // but I need help on the css, so we're going with this for now

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
                        <ConfigrGroup label="Appearance" level={1}>
                            <ConfigrSubgroup
                                label="Page Theme"
                                path={`appearance`}
                            >
                                <ConfigrSelect
                                    label="Theme"
                                    disabled={false}
                                    path={`appearance.cssThemeName`}
                                    options={appearanceUIOptions.themeNames.map(
                                        x => {
                                            return {
                                                label: x.label,
                                                value: x.value
                                            };
                                        }
                                    )}
                                    description="Choose a theme to easily change margins, borders, and other page settings."
                                />
                            </ConfigrSubgroup>
                            {
                                // This group of three possible messages...sometimes none of them shows, so there are four options...
                                // is very similar to the one in BookInfoIndicator.tsx. If you change one, you may need to change the other.
                                // In particular, the logic for which to show and the text of the messages should be kept in sync.
                                // Unfortunately, the formatting is quite different, and here we want to use the Warning/NoteBox
                                // components, but they don't apply there. Probably, when we internationalize and implement the
                                // TODO links, we'll want to refactor this to use at least one common components for
                                // Internationalized-messages-with-link. (I think all three message in both locations will use the
                                // same link, so that could be built into the component.)
                                // I'm not seeing a clean way to reuse the logic. Some sort of higher-order component might work,
                                // but I don't think the logic is complex enough to be worth it, when only used in two places.
                            }
                            {firstPossiblyLegacyCss && theme === "legacy-5-6" && (
                                <WarningBox>
                                    {`The "${firstPossiblyLegacyCss}" stylesheet of this book is incompatible with
                                    modern themes. Bloom is using it because the book is using the Legacy-5-6 theme. Click (TODO) for more information.`}
                                </WarningBox>
                            )}
                            {firstPossiblyLegacyCss ===
                                "customBookStyles.css" &&
                                theme !== "legacy-5-6" && (
                                    <div>
                                        <NoteBox>
                                            {`The "customBookStyles.css" stylesheet of this book is incompatible with
                                    modern themes. Bloom is currently ignoring it. If you don't need those
                                    customizations any more, you can delete your customBookStyles.css. Click (TODO) for more information.`}
                                        </NoteBox>

                                        <div
                                            css={css`
                                                display: flex;
                                                align-items: center;
                                                margin-top: 10px;
                                                margin-bottom: 10px;
                                            `}
                                            onClick={() =>
                                                deleteCustomBookStyles()
                                            }
                                        >
                                            <TrashIcon
                                                id="trashIcon"
                                                color="primary"
                                            />
                                            <div
                                                css={css`
                                                    color: ${kBloomBlue};
                                                `}
                                            >
                                                Delete customBookStyles.css
                                            </div>
                                        </div>
                                    </div>
                                )}
                            {firstPossiblyLegacyCss &&
                                firstPossiblyLegacyCss !==
                                    "customBookStyles.css" &&
                                theme !== "legacy-5-6" && (
                                    <NoteBox>
                                        {`"The ${firstPossiblyLegacyCss}" stylesheet of this book is incompatible with
                                    modern themes. Bloom is currently ignoring it. Click (TODO) for more information.`}
                                    </NoteBox>
                                )}
                            {
                                // This is not part of the group of three mutually exclusive messages above
                            }
                            {!firstPossiblyLegacyCss &&
                                (settingsToReturnLater as any)?.appearance
                                    ?.cssThemeName === "legacy-5-6" && (
                                    <NoteBox>
                                        {`The "Legacy" theme does not support Appearance settings.`}
                                    </NoteBox>
                                )}
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
                                    disabled={appearanceDisabled}
                                    path={`appearance.cover-title-L2-show`}
                                    label="Show Written Language 2 Title"
                                />
                                <ConfigrBoolean
                                    disabled={appearanceDisabled}
                                    path={`appearance.cover-title-L3-show`}
                                    label="Show Written Language 3 Title"
                                />
                                <ConfigrBoolean
                                    disabled={appearanceDisabled}
                                    path={`appearance.cover-languageName-show`}
                                    label="Show Language Name"
                                />
                                <ConfigrBoolean
                                    disabled={appearanceDisabled}
                                    path={`appearance.cover-topic-show`}
                                    label="Show Topic"
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
                            {/* note that this is used for bloomPUB and ePUB, but we don't have separate settings so we're putting them in bloomPUB and leaving it to c# code to use it for ePUB as well. */}
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
        <div>
            <ConfigrCustomObjectInput<Resolution>
                control={BloomResolutionSliderInner}
                {...props}
            ></ConfigrCustomObjectInput>
            <Div
                l10nKey="BookSettings.eBook.Image.MaxResolution.Directions"
                css={css`
                    padding: 0 10px;
                    font-size: 9pt;
                `}
            >
                Bloom reduces images to a maximum size to make books easier to
                view over poor internet connections and take up less space on
                phones.
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
        currentIndex = 1; // See BL-12803.
    }
    const current = sizes[currentIndex];
    const currentLabel = useL10n(
        current.l,
        `BookSettings.eBook.Image.MaxResolution.${current.l}`
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

// TODO: move this to config-r
const ConfigrCustomRow: React.FunctionComponent<React.PropsWithChildren<{}>> = props => {
    return (
        <ListItem
            css={css`
                flex-direction: column;
                align-items: flex-start;
            `}
        >
            {props.children}
        </ListItem>
    );
};
