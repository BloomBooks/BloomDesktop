import { css } from "@emotion/react";
import {
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrPane,
    ConfigrStatic,
} from "@sillsdev/config-r";
import { Button } from "@mui/material";
import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import {
    ColorDisplayButton,
    DialogResult,
} from "../../react_components/color-picking/colorPickerDialog";
import BloomButton from "../../react_components/bloomButton";
import { postJson, useApiObject } from "../../utils/bloomApi";
import {
    defaultSettings,
    getBloomLocalFileUrl,
    getDefaultAppBuilderIconUrl,
    IAppBuilderAppSettings,
    IAppBuilderAppSettingsApi,
    normalizeConfigrSettings,
    normalizeSettings,
} from "./appBuilderShared";

const kSettingsDialogFormWidth = "638px";

const AppBuilderColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (props) => {
    const mainColorLabel = useL10n(
        "Main Color",
        "PublishTab.Apps.SettingsDialog.MainColor",
    );

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value || defaultSettings.mainColor}
            localizedTitle={mainColorLabel}
            transparency={false}
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) {
                    props.onChange(newColor);
                }
            }}
        />
    );
};

const AppBuilderIconPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (props) => {
    const chooseButtonLabel = useL10n("Choose...", "Common.ChooseButton");
    const resetButtonLabel = useL10n(
        "Reset",
        "PublishTab.Apps.SettingsDialog.IconReset",
    );
    const chooseIconTitle = useL10n(
        "Choose App Icon",
        "PublishTab.Apps.SettingsDialog.IconChooseTitle",
    );
    const previewUrl = props.value
        ? getBloomLocalFileUrl(props.value)
        : getDefaultAppBuilderIconUrl();

    async function chooseIcon(): Promise<void> {
        const result = await postJson("fileIO/chooseFile", {
            title: chooseIconTitle,
            fileTypes: [
                {
                    name: "Images",
                    extensions: ["png", "jpg", "jpeg", "webp"],
                },
            ],
            defaultPath: props.value,
        });

        if (typeof result?.data === "string" && result.data) {
            props.onChange(result.data);
        }
    }

    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                gap: 8px;
                width: 100%;
            `}
        >
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    width: 52px;
                    height: 52px;
                    overflow: hidden;
                    background: #fff;
                    border: 1px solid #d7d7d7;
                    border-radius: 4px;
                    flex: 0 0 auto;
                `}
            >
                <img
                    key={previewUrl}
                    src={previewUrl}
                    alt=""
                    css={css`
                        max-width: 100%;
                        max-height: 100%;
                        object-fit: contain;
                    `}
                    onLoad={(event) => {
                        event.currentTarget.style.visibility = "visible";
                    }}
                    onError={(event) => {
                        event.currentTarget.style.visibility = "hidden";
                    }}
                />
            </div>
            <div
                css={css`
                    display: flex;
                    gap: 8px;
                    align-items: center;
                `}
            >
                <Button
                    type="button"
                    disabled={props.disabled}
                    variant="outlined"
                    title={chooseButtonLabel}
                    aria-label={chooseButtonLabel}
                    onClick={() => {
                        void chooseIcon();
                    }}
                >
                    ...
                </Button>
                <Button
                    type="button"
                    disabled={props.disabled || !props.value}
                    variant="text"
                    onClick={() => props.onChange("")}
                >
                    {resetButtonLabel}
                </Button>
            </div>
        </div>
    );
};

export const AppBuilderSettingsDialog: React.FunctionComponent<{
    canOpenInRab: boolean;
    onClose: () => void;
    onSaved: () => void;
}> = (props) => {
    const rawSettings = useApiObject<IAppBuilderAppSettingsApi>(
        "publish/rab/settings",
        defaultSettings,
    );
    const settings = normalizeSettings(rawSettings);
    // Configr returns the edited form state only when OK is pressed, so stash that payload until we convert and save it.
    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        string | object | undefined
    >(undefined);

    const dialogTitle = useL10n(
        "Settings",
        "PublishTab.Apps.SettingsDialog.Title",
    );
    const detailsPageLabel = useL10n(
        "Details",
        "PublishTab.Apps.SettingsDialog.DetailsPage",
    );
    const detailsGroupLabel = useL10n(
        "Details",
        "PublishTab.Apps.SettingsDialog.DetailsGroup",
    );
    const advancedPageLabel = useL10n(
        "Advanced",
        "PublishTab.Apps.SettingsDialog.AdvancedPage",
    );
    const appearanceGroupLabel = useL10n(
        "Appearance",
        "PublishTab.Apps.SettingsDialog.AppearanceGroup",
    );
    const appNameLabel = useL10n(
        "App Name",
        "PublishTab.Apps.SettingsDialog.AppName",
    );
    const mainColorLabel = useL10n(
        "Main Color",
        "PublishTab.Apps.SettingsDialog.MainColor",
    );
    const packageNameLabel = useL10n(
        "Package Name",
        "PublishTab.Apps.SettingsDialog.PackageName",
    );
    const iconLabel = useL10n("Icon", "PublishTab.Apps.SettingsDialog.Icon");
    const copyrightLabel = useL10n(
        "Copyright",
        "PublishTab.Apps.SettingsDialog.Copyright",
    );
    const packageNameDescription = useL10n(
        "The package name uniquely identifies the app. It is a lowercase dot-separated string without spaces, such as org.yourorg.appname.language or com.yourcompany.language.appname.",
        "PublishTab.Apps.SettingsDialog.PackageName.Description",
    );
    const packageNameTipsLabel = useL10n(
        "Tips on choosing the package name:",
        "PublishTab.Apps.SettingsDialog.PackageName.TipsLabel",
    );
    const packageNameTip1 = useL10n(
        "Start the package name by turning round the web address of your organisation, e.g. if your web address is 'sil.org', the package name can start with 'org.sil'.",
        "PublishTab.Apps.SettingsDialog.PackageName.Tip1",
    );
    const packageNameTip2 = useL10n(
        "After that, add additional strings to identify the app, such as 'org.sil.xyz.stories' where 'xyz' is the language code.",
        "PublishTab.Apps.SettingsDialog.PackageName.Tip2",
    );
    const packageNameTip3 = useL10n(
        "For test applications, you can use a temporary package name beginning with 'com.example', e.g. 'com.example.test1', 'com.example.stories', etc.",
        "PublishTab.Apps.SettingsDialog.PackageName.Tip3",
    );
    const packageNameTip4 = useL10n(
        "For additional help, please contact the digital publishing department of your organisation.",
        "PublishTab.Apps.SettingsDialog.PackageName.Tip4",
    );
    const packageNameHelpText = `${packageNameDescription} ${packageNameTipsLabel} 1. ${packageNameTip1} 2. ${packageNameTip2} 3. ${packageNameTip3} 4. ${packageNameTip4}`;
    const readingAppBuilderButtonLabel = useL10n(
        "Reading App Builder",
        "PublishTab.Apps.SettingsDialog.ReadingAppBuilder",
    );

    function getSettingsToSave(): IAppBuilderAppSettings {
        // Start from the current API-backed settings so untouched fields survive a partial Configr edit.
        return (
            (normalizeConfigrSettings(
                settingsToReturnLater,
            ) as IAppBuilderAppSettings) ?? settings
        );
    }

    async function saveSettings(
        openReadingAppBuilder?: boolean,
    ): Promise<void> {
        const settingsToSave = getSettingsToSave();
        await postJson("publish/rab/settings", settingsToSave);
        props.onSaved();
        props.onClose();

        if (openReadingAppBuilder && props.canOpenInRab) {
            await postJson("publish/rab/open", {});
        }
    }

    return (
        <BloomDialog
            open={true}
            dialogFrameProvidedExternally={false}
            onClose={props.onClose}
            onCancel={() => props.onClose()}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    &:first-child {
                        margin-top: 0;
                    }
                    overflow-y: auto;

                    form {
                        overflow-y: auto;
                        height: 600px;
                        width: ${kSettingsDialogFormWidth};

                        #groups {
                            margin-right: 10px;
                        }
                    }

                    a {
                        color: ${kBloomBlue};
                    }
                `}
            >
                <ConfigrPane
                    key={JSON.stringify(settings)}
                    label={dialogTitle}
                    showAppBar={false}
                    showSearch={true}
                    initialValues={settings}
                    themeOverrides={{
                        palette: {
                            primary: { main: kBloomBlue },
                        },
                    }}
                    onChange={(nextSettings) => {
                        setSettingsToReturnLater(nextSettings);
                    }}
                >
                    <ConfigrPage
                        label={detailsPageLabel}
                        pageKey="details"
                        topLevel={true}
                    >
                        <ConfigrGroup label={detailsGroupLabel}>
                            <ConfigrInput label={appNameLabel} path="appName" />
                            <ConfigrInput
                                label={packageNameLabel}
                                path="packageName"
                                description={packageNameHelpText}
                            />
                            <ConfigrInput
                                label={copyrightLabel}
                                path="copyright"
                            />
                        </ConfigrGroup>
                        <ConfigrGroup label={appearanceGroupLabel}>
                            <ConfigrCustomStringInput
                                label={mainColorLabel}
                                path="mainColor"
                                control={AppBuilderColorPickerForConfigr}
                            />
                            <ConfigrCustomStringInput
                                label={iconLabel}
                                path="iconPath"
                                control={AppBuilderIconPickerForConfigr}
                            />
                        </ConfigrGroup>
                    </ConfigrPage>
                    <ConfigrPage
                        label={advancedPageLabel}
                        pageKey="advanced"
                        topLevel={true}
                    >
                        <ConfigrGroup>
                            <ConfigrStatic>
                                <div
                                    css={css`
                                        display: flex;
                                        flex-direction: column;
                                        gap: 12px;
                                    `}
                                >
                                    <BloomButton
                                        enabled={props.canOpenInRab}
                                        l10nKey="PublishTab.Apps.SettingsDialog.ReadingAppBuilder"
                                        hasText={true}
                                        variant="contained"
                                        onClick={() => {
                                            void saveSettings(true);
                                        }}
                                    >
                                        {readingAppBuilderButtonLabel}
                                    </BloomButton>
                                    <div
                                        css={css`
                                            font-size: 0.9em;
                                            color: #555;
                                        `}
                                    >
                                        {useL10n(
                                            "After Setup creates the Reading App Builder project, use the button below to open that project in Reading App Builder and edit its advanced settings there.",
                                            "PublishTab.Apps.SettingsDialog.AdvancedPage.Description",
                                        )}
                                    </div>
                                </div>
                            </ConfigrStatic>
                        </ConfigrGroup>
                    </ConfigrPage>
                </ConfigrPane>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        void saveSettings();
                    }}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
