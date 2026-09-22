import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrValues,
    ConfigrGroup,
    ConfigrPage,
    ConfigrPane,
    ConfigrStatic,
} from "@sillsdev/config-r";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { WarningBox } from "../react_components/boxes";
import { useL10n } from "../react_components/l10nHooks";
import { get, post, postJson } from "../utils/bloomApi";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { changedRestartPaths } from "./collectionSettingsRestart";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsSaveResult,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";

// Same fixed size as the Book Settings dialog (kBookSettingsDialogWidthPx/HeightPx there),
// so the two settings dialogs feel like one family.
const kCollectionSettingsDialogWidthPx = 900;
const kCollectionSettingsDialogHeightPx = 720;
const kConfigrPaneClassName = "collection-settings-configr-pane";

// Temporary content for every page. Each of the seven tab cards replaces its page's group with
// real controls, so this text is deliberately plain English and is never localized.
const PagePlaceholder: React.FunctionComponent = () => (
    <div
        css={css`
            font-size: 0.9em;
            color: #555;
        `}
    >
        Settings for this section are not available yet.
    </div>
);

export const CollectionSettingsDialog: React.FunctionComponent = () => {
    const { openingEvent, closeDialog, propsForBloomDialog } =
        useEventLaunchedBloomDialog("CollectionSettingsDialog");

    // C# names the page to open on in the LaunchDialog message; it sends "" for a plain open.
    const initialPageKey =
        (openingEvent?.initialPageKey as string | undefined) || undefined;

    const [loadedSettings, setLoadedSettings] =
        React.useState<ICollectionSettingsResponse>();
    const [currentValues, setCurrentValues] =
        React.useState<ICollectionSettingsValues>();
    const [saveErrorMessage, setSaveErrorMessage] = React.useState<string>();
    const [saving, setSaving] = React.useState(false);

    // Config-r can call onChange while rendering, so state updates from it are deferred; the OK
    // handler reads this ref to be sure it has the newest values.
    const latestValuesRef = React.useRef<ICollectionSettingsValues>();

    // The GET also opens the editing session on the C# side, so it must run on every open.
    React.useEffect(() => {
        if (!propsForBloomDialog.open) {
            // Forget the closing session's values. Config-r captures initialValues when the pane
            // mounts, so if we left them here the next open would mount the pane on the previous
            // session's data and go on editing (and saving) that until the new GET arrived.
            latestValuesRef.current = undefined;
            setLoadedSettings(undefined);
            setCurrentValues(undefined);
            setSaveErrorMessage(undefined);
            setSaving(false);
            return;
        }
        get("collection/settings", (result) => {
            const response = result.data as ICollectionSettingsResponse;
            latestValuesRef.current = response.values;
            setLoadedSettings(response);
            setCurrentValues(response.values);
            setSaveErrorMessage(undefined);
        });
    }, [propsForBloomDialog.open]);

    const dialogTitle = useL10n(
        "Collection Settings",
        "CollectionSettingsDialog.Title",
    );
    const languagesLabel = useL10n(
        "Languages",
        "CollectionSettingsDialog.LanguageTab.LanguageTabLabel",
    );
    const frontBackMatterLabel = useL10n(
        "Front & Back Matter",
        "CollectionSettingsDialog.FrontBackMatterPage",
    );
    const subscriptionLabel = useL10n(
        "Subscription",
        "CollectionSettingsDialog.SubscriptionPage",
    );
    const teamCollectionLabel = useL10n(
        "Team Collection",
        "TeamCollection.TeamCollection",
    );
    const bloomLibraryLabel = useL10n(
        "Bloom Library",
        "CollectionSettingsDialog.BloomLibraryPage",
    );
    const advancedLabel = useL10n("Advanced", "Common.Advanced");
    const experimentalLabel = useL10n(
        "Experimental",
        "CollectionSettingsDialog.ExperimentalPage",
    );
    const restartMessage = useL10n(
        "Bloom will close and re-open this project with the new settings.",
        "CollectionSettingsDialog.RestartMessage",
    );

    // C# names these pageKeys when it asks us to open on a particular page.
    const pages = [
        { pageKey: "languages", label: languagesLabel },
        { pageKey: "frontBackMatter", label: frontBackMatterLabel },
        { pageKey: "subscription", label: subscriptionLabel },
        { pageKey: "teamCollection", label: teamCollectionLabel },
        { pageKey: "bloomLibrary", label: bloomLibraryLabel },
        { pageKey: "advanced", label: advancedLabel },
        { pageKey: "experimental", label: experimentalLabel },
    ];

    const needsRestart =
        loadedSettings !== undefined &&
        currentValues !== undefined &&
        changedRestartPaths(
            loadedSettings.values,
            currentValues,
            loadedSettings.restartPaths,
        ).length > 0;

    function saveAndCloseDialog() {
        // The first POST ends the editing session, so a second one would arrive with nothing
        // pending; block the button for the moment the save is in flight.
        setSaving(true);
        // Always post, even if these values are unchanged: reused components (subscription, team
        // collection, bookshelf) send their edits through their own endpoints, and this POST
        // applies everything pending.
        postJson(
            "collection/settings",
            latestValuesRef.current,
            (result) => {
                const saveResult = result.data as ICollectionSettingsSaveResult;
                if (saveResult.errorMessage) {
                    setSaveErrorMessage(saveResult.errorMessage);
                    setSaving(false);
                    return;
                }
                // C# performs the restart itself if one is needed.
                closeDialog();
            },
            // If the save fails outright, the user keeps whatever they were editing and can try
            // OK again; leaving the button disabled would strand them with no way but Cancel.
            () => setSaving(false),
        );
    }

    function cancelAndCloseDialog() {
        post("collection/settings/cancel");
        closeDialog();
    }

    return (
        <BloomDialog
            css={css`
                height: 100%;
                box-sizing: border-box;

                .MuiDialog-paper {
                    width: ${kCollectionSettingsDialogWidthPx}px;
                    height: ${kCollectionSettingsDialogHeightPx}px;
                }
            `}
            {...propsForBloomDialog}
            onClose={cancelAndCloseDialog}
            onCancel={cancelAndCloseDialog}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle}></DialogTitle>
            <DialogMiddle
                css={css`
                    overflow-y: hidden;
                    min-height: 0;

                    .${kConfigrPaneClassName} {
                        height: 100%;
                        min-height: 0;
                    }

                    // Let config-r consume the available dialog height so the button row stays
                    // pinned to the bottom and only the page contents scroll.
                    form {
                        overflow-y: auto;
                        height: 100%;
                        min-height: 0;
                        width: 100%;
                        box-sizing: border-box;
                        #groups {
                            margin-right: 10px; // make room for the scrollbar
                        }
                    }

                    a {
                        color: ${kBloomBlue};
                    }
                `}
            >
                {loadedSettings && (
                    <ConfigrPane
                        className={kConfigrPaneClassName}
                        label={dialogTitle}
                        showAppBar={false}
                        showSearch={false}
                        initialValues={
                            loadedSettings.values as unknown as ConfigrValues
                        }
                        themeOverrides={{
                            // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                            palette: {
                                primary: { main: kBloomBlue },
                            },
                        }}
                        initiallySelectedTopLevelPageKey={initialPageKey}
                        onChange={(newValues) => {
                            const values =
                                newValues as unknown as ICollectionSettingsValues;
                            latestValuesRef.current = values;
                            // Config-r may call onChange while rendering, so defer the state update.
                            window.setTimeout(() => {
                                setCurrentValues(values);
                            }, 0);
                        }}
                    >
                        {pages.map((page) => (
                            <ConfigrPage
                                key={page.pageKey}
                                label={page.label}
                                pageKey={page.pageKey}
                                topLevel={true}
                            >
                                <ConfigrGroup label={page.label}>
                                    <ConfigrStatic>
                                        <PagePlaceholder />
                                    </ConfigrStatic>
                                </ConfigrGroup>
                            </ConfigrPage>
                        ))}
                    </ConfigrPane>
                )}
            </DialogMiddle>
            {saveErrorMessage && <WarningBox>{saveErrorMessage}</WarningBox>}
            <DialogBottomButtons>
                {needsRestart && (
                    <DialogBottomLeftButtons>
                        <div
                            css={css`
                                align-self: center;
                            `}
                        >
                            {restartMessage}
                        </div>
                    </DialogBottomLeftButtons>
                )}
                <DialogOkButton
                    default={true}
                    enabled={currentValues !== undefined && !saving}
                    l10nKey={
                        needsRestart
                            ? "CollectionSettingsDialog.Restart"
                            : undefined
                    }
                    englishText={needsRestart ? "Restart" : undefined}
                    onClick={saveAndCloseDialog}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
