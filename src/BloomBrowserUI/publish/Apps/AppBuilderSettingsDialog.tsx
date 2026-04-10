import { css } from "@emotion/react";
import axios, { CancelTokenSource } from "axios";
import {
    ConfigrInput,
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrPage,
    ConfigrPane,
} from "@sillsdev/config-r";
import { CircularProgress, MenuItem, Select } from "@mui/material";
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
import BloomButton from "../../react_components/bloomButton";
import { postDataWithConfigAsync } from "../../utils/bloomApi";
import {
    applyDefaultAppBuilderIconChoice,
    defaultSettings,
    fetchAppBuilderIconChoices,
    getAppBuilderPackageNameValidationIssue,
    IAppBuilderAppSettings,
    saveAppBuilderSettings,
    useAppBuilderAppSettingsForPath,
} from "./appBuilderAppDef";
import { AppBuilderIconChooserForConfigr } from "./AppBuilderIconChooser";
import { normalizeConfigrSettings } from "./appBuilderShared";

const kSettingsDialogFormWidth = "638px";
const kStartingReadingAppBuilderDialogWidth = "420px";

const StartingReadingAppBuilderDialog: React.FunctionComponent<{
    onCancel: () => void;
}> = (props) => {
    const startingReadingAppBuilderLabel = useL10n(
        "Starting Reading App Builder...",
        "PublishTab.Apps.SettingsDialog.StartingReadingAppBuilder",
    );

    return (
        <BloomDialog
            open={true}
            dialogFrameProvidedExternally={false}
            onClose={props.onCancel}
            onCancel={props.onCancel}
            draggable={false}
            maxWidth={false}
        >
            <DialogMiddle
                css={css`
                    width: ${kStartingReadingAppBuilderDialogWidth};
                    min-height: 220px;
                    align-items: center;
                    justify-content: center;
                    gap: 18px;
                    text-align: center;
                    padding: 24px 36px 0;
                `}
                aria-live="polite"
            >
                <CircularProgress size={72} thickness={4} />
                <div
                    css={css`
                        font-size: 1.2rem;
                        font-weight: 600;
                    `}
                >
                    {startingReadingAppBuilderLabel}
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCancelButton default={true} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

const kRabColorSchemes = [
    { name: "White", swatchColor: "#FFFFFF" },
    { name: "Dark Green", swatchColor: "#083F0E" },
    { name: "Dark Blue", swatchColor: "#000B63" },
    { name: "Royal Blue", swatchColor: "#0D47A1" },
    { name: "Indigo", swatchColor: "#3F51B5" },
    { name: "Dark Indigo", swatchColor: "#1A237E" },
    { name: "Blue", swatchColor: "#2196F3" },
    { name: "Dark Red", swatchColor: "#6C0000" },
    { name: "Purple", swatchColor: "#9C27B0" },
    { name: "Deep Purple", swatchColor: "#673AB7" },
    { name: "Light Blue", swatchColor: "#03A9F4" },
    { name: "Cyan", swatchColor: "#00BCD4" },
    { name: "Teal", swatchColor: "#009688" },
    { name: "Green", swatchColor: "#4CAF50" },
    { name: "India Green", swatchColor: "#138808" },
    { name: "Pakistan Green", swatchColor: "#006600" },
    { name: "Asparagus Green", swatchColor: "#87A96B" },
    { name: "Dark Olive Green", swatchColor: "#556B2F" },
    { name: "Shamrock Green", swatchColor: "#009E60" },
    { name: "Light Green", swatchColor: "#8BC34A" },
    { name: "Lime", swatchColor: "#C0CA33" },
    { name: "Amber", swatchColor: "#FFC107" },
    { name: "Orange", swatchColor: "#FF9800" },
    { name: "Deep Orange", swatchColor: "#FF5722" },
    { name: "Dark Orange", swatchColor: "#AD4428" },
    { name: "Red", swatchColor: "#F44336" },
    { name: "Brown", swatchColor: "#4E342E" },
    { name: "Chocolate", swatchColor: "#7B3F00" },
    { name: "Black", swatchColor: "#000000" },
];

const AppBuilderColorSchemeSwatch: React.FunctionComponent<{
    color: string;
}> = (props) => {
    return (
        <span
            css={css`
                width: 30px;
                height: 16px;
                border-radius: 3px;
                border: 1px solid rgba(0, 0, 0, 0.18);
                background: ${props.color};
                flex: 0 0 auto;
            `}
        />
    );
};

const AppBuilderColorSchemeLabel: React.FunctionComponent<{
    name: string;
    swatchColor: string;
}> = (props) => {
    return (
        <span
            css={css`
                display: inline-flex;
                align-items: center;
                gap: 10px;
            `}
        >
            <AppBuilderColorSchemeSwatch color={props.swatchColor} />
            <span>{props.name}</span>
        </span>
    );
};

const AppBuilderColorSchemeChooserForConfigr: React.FunctionComponent<{
    value: string;
    disabled?: boolean;
    onChange: (value: string) => void;
}> = (props) => {
    const selectedScheme =
        kRabColorSchemes.find((scheme) => scheme.name === props.value) ??
        kRabColorSchemes.find(
            (scheme) => scheme.name === defaultSettings.colorScheme,
        ) ??
        kRabColorSchemes[0];

    return (
        <Select
            value={props.value || selectedScheme.name}
            disabled={props.disabled}
            size="small"
            css={css`
                min-width: 280px;
                width: 100%;
                background: white;
            `}
            onChange={(event) => {
                props.onChange(event.target.value);
            }}
            renderValue={(selected) => {
                const scheme =
                    kRabColorSchemes.find((item) => item.name === selected) ??
                    selectedScheme;

                return (
                    <AppBuilderColorSchemeLabel
                        name={scheme.name}
                        swatchColor={scheme.swatchColor}
                    />
                );
            }}
        >
            {kRabColorSchemes.map((scheme) => (
                <MenuItem key={scheme.name} value={scheme.name}>
                    <AppBuilderColorSchemeLabel
                        name={scheme.name}
                        swatchColor={scheme.swatchColor}
                    />
                </MenuItem>
            ))}
        </Select>
    );
};

const ReadingAppBuilderButtonForConfigr: React.FunctionComponent<{
    value: string;
    onChange: (value: string) => void;
    onOpen: () => void;
    buttonLabel: string;
    canOpenInRab: boolean;
}> = (props) => {
    return (
        <div
            css={css`
                width: 100%;
                display: flex;
                justify-content: flex-end;
            `}
        >
            <BloomButton
                enabled={props.canOpenInRab}
                l10nKey="PublishTab.Apps.SettingsDialog.ReadingAppBuilder"
                hasText={true}
                variant="contained"
                onClick={props.onOpen}
            >
                {props.buttonLabel}
            </BloomButton>
        </div>
    );
};

export const AppBuilderSettingsDialog: React.FunctionComponent<{
    appDefPath: string;
    canOpenInRab: boolean;
    onClose: () => void;
    onSaved: () => void;
}> = (props) => {
    const settings = useAppBuilderAppSettingsForPath(props.appDefPath);
    const [isStartingReadingAppBuilder, setIsStartingReadingAppBuilder] =
        React.useState(false);
    const openReadingAppBuilderCancelSource = React.useRef<
        CancelTokenSource | undefined
    >(undefined);
    const cancelStartingReadingAppBuilderRef = React.useRef(false);
    // Configr returns the edited form state only when OK is pressed, so stash that payload until we convert and save it.
    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        string | object | undefined
    >(undefined);

    const dialogTitle = useL10n(
        "App Settings",
        "PublishTab.Apps.SettingsDialog.Title",
    );
    const detailsGroupLabel = useL10n(
        "Basics",
        "PublishTab.Apps.SettingsDialog.DetailsGroup",
    );
    const appearanceGroupLabel = useL10n(
        "Appearance",
        "PublishTab.Apps.SettingsDialog.AppearanceGroup",
    );
    const appNameLabel = useL10n(
        "App Name",
        "PublishTab.Apps.SettingsDialog.AppName",
    );
    const colorSchemeLabel = useL10n(
        "Color Scheme",
        "PublishTab.Apps.SettingsDialog.ColorScheme",
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
    const aboutLabel = useL10n("About", "PublishTab.Apps.SettingsDialog.About");
    const packageNameDescription = useL10n(
        "Template: org.<yourorg>.<language-code>.<appname>. E.g. org.sil.xyz.stories. For additional help please contact the Digital Publishing Department of your organization.",
        "PublishTab.Apps.SettingsDialog.PackageName.Description",
    );
    const packageNameInvalidMessage = useL10n(
        "Use a lowercase dot-separated package name with no spaces, for example org.sil.xyz.stories",
        "PublishTab.Apps.SettingsDialog.PackageName.Invalid",
    );
    const readingAppBuilderButtonLabel = useL10n(
        "Reading App Builder",
        "PublishTab.Apps.SettingsDialog.ReadingAppBuilder",
    );
    const moreSettingsLabel = useL10n(
        "More Settings",
        "PublishTab.Apps.SettingsDialog.MoreSettings",
    );
    const readingAppBuilderDescription = useL10n(
        "Use this to do more customization of your app. Make sure to click Save and quit Reading App Builder before returning to Bloom.",
        "PublishTab.Apps.SettingsDialog.RunRAB.Description",
    );
    const openReadingAppBuilderControl: React.FunctionComponent<{
        value: string;
        disabled?: boolean;
        onChange: (value: string) => void;
    }> = (controlProps) => {
        return (
            <ReadingAppBuilderButtonForConfigr
                {...controlProps}
                buttonLabel={readingAppBuilderButtonLabel}
                canOpenInRab={props.canOpenInRab}
                onOpen={() => {
                    void saveSettingsAndOpenReadingAppBuilder();
                }}
            />
        );
    };

    function validatePackageName(packageName: string | number): true | string {
        const issue = getAppBuilderPackageNameValidationIssue(
            packageName.toString(),
        );
        if (issue === "invalid") {
            return packageNameInvalidMessage;
        }

        return true;
    }

    function getSettingsToSave(): IAppBuilderAppSettings {
        // Start from the current API-backed settings so untouched fields survive a partial Configr edit.
        return (
            (normalizeConfigrSettings(
                settingsToReturnLater,
            ) as IAppBuilderAppSettings) ?? settings
        );
    }

    function finishStartingReadingAppBuilder(): void {
        openReadingAppBuilderCancelSource.current = undefined;
        setIsStartingReadingAppBuilder(false);
    }

    function cancelStartingReadingAppBuilder(): void {
        cancelStartingReadingAppBuilderRef.current = true;
        openReadingAppBuilderCancelSource.current?.cancel(
            "User canceled starting Reading App Builder.",
        );
        finishStartingReadingAppBuilder();
    }

    function wasStartingReadingAppBuilderCanceled(): boolean {
        return cancelStartingReadingAppBuilderRef.current;
    }

    async function openReadingAppBuilder(): Promise<boolean> {
        const cancelSource = axios.CancelToken.source();
        openReadingAppBuilderCancelSource.current = cancelSource;

        try {
            const response = await postDataWithConfigAsync(
                "publish/rab/open",
                {},
                {
                    cancelToken: cancelSource.token,
                    headers: {
                        "Content-Type": "application/json; charset=utf-8",
                    },
                },
            );
            return !!response;
        } catch (error) {
            if (axios.isCancel(error)) {
                return false;
            }

            throw error;
        } finally {
            openReadingAppBuilderCancelSource.current = undefined;
        }
    }

    async function persistSettings(): Promise<boolean> {
        const iconChoices = await fetchAppBuilderIconChoices();
        const settingsToSave = applyDefaultAppBuilderIconChoice(
            getSettingsToSave(),
            iconChoices,
        );
        const didSave = await saveAppBuilderSettings(
            props.appDefPath,
            settingsToSave,
        );
        if (!didSave) {
            return false;
        }

        props.onSaved();
        return true;
    }

    async function saveSettingsAndClose(): Promise<void> {
        const didSave = await persistSettings();
        if (!didSave) {
            return;
        }

        props.onClose();
    }

    async function saveSettingsAndOpenReadingAppBuilder(): Promise<void> {
        if (!props.canOpenInRab) {
            return;
        }

        cancelStartingReadingAppBuilderRef.current = false;
        setIsStartingReadingAppBuilder(true);

        try {
            const didSave = await persistSettings();
            if (!didSave || wasStartingReadingAppBuilderCanceled()) {
                finishStartingReadingAppBuilder();
                return;
            }

            const didOpen = await openReadingAppBuilder();
            if (!didOpen || wasStartingReadingAppBuilderCanceled()) {
                finishStartingReadingAppBuilder();
                return;
            }

            props.onClose();
        } catch (error) {
            finishStartingReadingAppBuilder();
            throw error;
        }
    }

    return (
        <>
            <BloomDialog
                open={true}
                dialogFrameProvidedExternally={false}
                onClose={props.onClose}
                onCancel={props.onClose}
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
                        initialValues={
                            settings as unknown as Record<string, unknown>
                        }
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
                            label={""}
                            pageKey="details"
                            topLevel={true}
                        >
                            <ConfigrGroup label={detailsGroupLabel}>
                                <ConfigrInput
                                    label={appNameLabel}
                                    path="appName"
                                    required={true}
                                    charactersWide={40}
                                />
                                <ConfigrInput
                                    label={packageNameLabel}
                                    path="packageName"
                                    description={packageNameDescription}
                                    required={true}
                                    charactersWide={40}
                                    validation={validatePackageName}
                                />
                                <ConfigrInput
                                    label={copyrightLabel}
                                    path="copyright"
                                    required={true}
                                    charactersWide={40}
                                />
                                <ConfigrInput
                                    label={aboutLabel}
                                    path="about"
                                    required={true}
                                    charactersWide={50}
                                    allowNewLines={true}
                                    minLinesToShow={2}
                                    maxLinesToShowBeforeScrolling={5}
                                />
                            </ConfigrGroup>
                            <ConfigrGroup label={appearanceGroupLabel}>
                                <ConfigrCustomStringInput
                                    label={colorSchemeLabel}
                                    path="colorScheme"
                                    control={
                                        AppBuilderColorSchemeChooserForConfigr
                                    }
                                />
                                <ConfigrCustomStringInput
                                    label={iconLabel}
                                    path="iconPath"
                                    control={AppBuilderIconChooserForConfigr}
                                />
                            </ConfigrGroup>
                            <ConfigrGroup>
                                <ConfigrCustomStringInput
                                    label={moreSettingsLabel}
                                    path="openReadingAppBuilder"
                                    description={readingAppBuilderDescription}
                                    overrideValue=""
                                    control={openReadingAppBuilderControl}
                                />
                            </ConfigrGroup>
                        </ConfigrPage>
                    </ConfigrPane>
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogOkButton
                        default={true}
                        onClick={() => {
                            void saveSettingsAndClose();
                        }}
                    />
                    <DialogCancelButton />
                </DialogBottomButtons>
            </BloomDialog>
            {isStartingReadingAppBuilder && (
                <StartingReadingAppBuilderDialog
                    onCancel={cancelStartingReadingAppBuilder}
                />
            )}
        </>
    );
};
