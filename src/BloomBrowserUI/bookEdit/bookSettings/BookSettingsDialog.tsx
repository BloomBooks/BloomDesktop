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
import { setCssDisplayMessages } from "../../react_components/BookInfoIndicator";

let isOpenAlready = false;

type IPageStyle = { label: string; value: string };
type IPageStyles = Array<IPageStyle>;
type IAppearanceUIOptions = {
    firstPossiblyOffendingCssFile?: string;
    substitutedCssFile?: string;
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
    firstPossiblyOffendingCssFile?: string;
}

// Stuff we get from the book/settings/overrides api.
// The branding and xmatter objects contain the corresponding settings,
// using the same keys as appearance.json. Currently the values are all
// booleans.
interface IOverrideInformation {
    branding: object;
    xmatter: object;
    brandingName: string;
    xmatterName: string;
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

    const overrideInformation: IOverrideInformation | undefined = useApiObject<
        IOverrideInformation
    >("book/settings/overrides", {
        xmatter: {},
        branding: {},
        xmatterName: "",
        brandingName: ""
    });
    const xmatterDescription = useL10n(
        "Locked by {0} Front/Back matter",
        "BookSettings.LockedByXMatter",
        "",
        overrideInformation?.xmatterName
    );

    const brandingDescription = useL10n(
        "Locked by {0} Branding",
        "BookSettings.LockedByBranding",
        "",
        overrideInformation?.brandingName
    );

    // This is a helper function to make it easier to pass the override information
    function getAdditionalProps<T>(subPath: string) {
        // some properties will be overridden by branding and/or xmatter
        const xmatterOverride: T | undefined =
            overrideInformation?.xmatter?.[subPath];
        const brandingOverride = overrideInformation?.branding?.[subPath];
        const override = xmatterOverride ?? brandingOverride;
        // nb: xmatterOverride can be boolean, hence the need to spell out !==undefined
        let description =
            xmatterOverride !== undefined ? xmatterDescription : undefined;
        if (!description) {
            // xmatter wins if both are present
            description =
                brandingOverride !== undefined
                    ? brandingDescription
                    : undefined;
        }
        // make a an object that can be spread as props in any of the Configr controls
        return {
            path: "appearance." + subPath,
            disabled: appearanceDisabled,
            overrideValue: override as T,
            // if we're disabling all appearance controls (e.g. because we're in legacy), don't list a second reason for this overload
            overrideDescription: appearanceDisabled ? "" : description
        };
    }

    const [settingsString] = useApiStringState(
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
    // firstPossiblyOffendingCssFile. For now it just behaves as if there are now no conflicts.
    // One possible approach is to have the server return the new firstPossiblyOffendingCssFile
    // as the result of the deleteCustomBookStyles call.
    const [theme, setTheme] = React.useState("");
    const [
        firstPossiblyOffendingCssFile,
        setFirstPossiblyOffendingCssFile
    ] = React.useState("");
    const [substitutedCssFile, setSubstitutedCssFile] = React.useState("");

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
        setFirstPossiblyOffendingCssFile(
            appearanceUIOptions?.firstPossiblyOffendingCssFile ?? ""
        );
        setSubstitutedCssFile(appearanceUIOptions?.substitutedCssFile ?? "");
    }, [appearanceUIOptions]);

    const bookSettingsTitle = useL10n("Book Settings", "BookSettings.Title");
    React.useEffect(() => {
        if (settings && (settings as any).appearance) {
            const liveSettings = settingsToReturnLater || settings;
            // when we're in legacy, we're just going to disable all the appearance controls
            setAppearanceDisabled(
                (liveSettings as any)?.appearance?.cssThemeName === "legacy-5-6"
            );
            setTheme((liveSettings as IBookSettings)?.appearance?.cssThemeName);
        }
    }, [settings, settingsToReturnLater]);

    const deleteCustomBookStyles = () => {
        post(
            `book/settings/deleteCustomBookStyles?file=${firstPossiblyOffendingCssFile}`
        );
        setFirstPossiblyOffendingCssFile("");
        setSubstitutedCssFile("");
    };

    const {
        warningMessage,
        infoMessageForPossibleDelete,
        infoMessage
    } = setCssDisplayMessages(
        theme,
        substitutedCssFile,
        firstPossiblyOffendingCssFile
    );

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
                        // This odd width was chosen to make the customBookStyles alert box format nicely.
                        // See BL-12956. It's not that important, but I don't think anything else is affected
                        // much by the exact witdh.
                        width: 638px;
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
                        onChange={s => {
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
                                // The logic and methods are shared with BookInfoIndicator.tsx, in the function
                                // setCssDisplayMessages.
                            }
                            {warningMessage && (
                                <WarningBox>{warningMessage}</WarningBox>
                            )}
                            {infoMessageForPossibleDelete && (
                                <NoteBox>
                                    <div>
                                        {infoMessageForPossibleDelete}
                                        <div
                                            css={css`
                                                display: flex;
                                                align-items: center;
                                                // The way it comes out in English, we'd be better off without this, or even
                                                // some negative margin. But a translation may produce a last line of the
                                                // main message
                                                margin-top: 2px;
                                                justify-content: flex-end;
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
                                                Delete{" "}
                                                {firstPossiblyOffendingCssFile}
                                            </div>
                                        </div>
                                    </div>
                                </NoteBox>
                            )}
                            {infoMessage && <NoteBox>{infoMessage}</NoteBox>}
                            {
                                // This is not part of the group of three mutually exclusive messages above
                            }
                            {!firstPossiblyOffendingCssFile &&
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
                                    label="Show Written Language 2 Title"
                                    {...getAdditionalProps<boolean>(
                                        "cover-title-L2-show"
                                    )}
                                />
                                <ConfigrBoolean
                                    label="Show Written Language 3 Title"
                                    {...getAdditionalProps<boolean>(
                                        `cover-title-L3-show`
                                    )}
                                />
                                <ConfigrBoolean
                                    label="Show Language Name"
                                    {...getAdditionalProps<boolean>(
                                        `cover-languageName-show`
                                    )}
                                />
                                <ConfigrBoolean
                                    label="Show Topic"
                                    {...getAdditionalProps<boolean>(
                                        `cover-topic-show`
                                    )}
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

function newFunction(
    theme: string,
    substitutedCssFile: string,
    firstPossiblyOffendingCssFile: string
) {
    let infoMessageForPossibleDelete: string = "";
    let warningMessage: string = "";
    let infoMessage: string = "";
    if (theme !== "legacy-5-6") {
        if (
            substitutedCssFile &&
            firstPossiblyOffendingCssFile === substitutedCssFile
        ) {
            infoMessageForPossibleDelete = `Bloom found a known version of ${firstPossiblyOffendingCssFile} in this book
            and replaced it with a modern theme. You can delete it unless you still
            need to publish the book from an earlier version of Bloom.`;
        } else if (
            !substitutedCssFile &&
            (firstPossiblyOffendingCssFile === "customBookStyles.css" ||
                firstPossiblyOffendingCssFile === "customCollectionStyles.css")
        ) {
            infoMessageForPossibleDelete = `The ${firstPossiblyOffendingCssFile} stylesheet of this book is incompatible with
            modern themes. Bloom is currently ignoring it. If you don't need those
            customizations any more, you can delete your ${firstPossiblyOffendingCssFile}. Click (TODO) for more information.`;
        } else if (
            !substitutedCssFile &&
            firstPossiblyOffendingCssFile &&
            firstPossiblyOffendingCssFile !== "customBookStyles.css" &&
            firstPossiblyOffendingCssFile !== "customCollectionStyles.css"
        ) {
            infoMessage = `The "${firstPossiblyOffendingCssFile}" stylesheet of this book is incompatible with
                    modern themes. Bloom is currently ignoring it. Click (TODO) for more information.`;
        }
    } else {
        if (firstPossiblyOffendingCssFile && theme === "legacy-5-6") {
            warningMessage = `The "${firstPossiblyOffendingCssFile}" stylesheet of this book is incompatible with
            modern themes. Bloom is using it because the book is using the Legacy-5-6 theme. Click (TODO) for more information.`;
        }
    }
    return { warningMessage, infoMessageForPossibleDelete, infoMessage };
}

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
