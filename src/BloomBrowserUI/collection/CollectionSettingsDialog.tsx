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
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import {
    ConfigrDialogMiddle,
    kConfigrDialogSizeCss,
    kConfigrPaneClassName,
    kConfigrThemeOverrides,
} from "../react_components/ConfigrDialogMiddle";
import { useEventLaunchedBloomDialog } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogCloseButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { get, postJson } from "../utils/bloomApi";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";
import {
    advancedValuesToSave,
    useAdvancedPage,
} from "./settingsPages/AdvancedPage";
import { useExperimentalPage } from "./settingsPages/ExperimentalPage";

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

const kCollectionSettingsDialogId = "CollectionSettingsDialog";

// Walks a dotted restart path such as "languages.language3.tag" into the values. A path through a
// null branch (no third language) yields undefined, so adding or removing one shows as a change.
function valueAtPath(values: ICollectionSettingsValues, path: string): unknown {
    return path
        .split(".")
        .reduce<unknown>(
            (current, key) =>
                (current as Record<string, unknown> | null | undefined)?.[key],
            values,
        );
}

/**
 * Opens the Collection Settings dialog, on the page named by initialPageKey if one is given.
 * App renders the dialog on every workspace tab, so this works from any of them, with no trip
 * through C#. C# can open it too, by sending LaunchDialog with the same id over the web socket.
 */
export function showCollectionSettingsDialog(initialPageKey?: string) {
    document.dispatchEvent(
        new CustomEvent("LaunchDialog", {
            detail: { id: kCollectionSettingsDialogId, initialPageKey },
        }),
    );
}

export const CollectionSettingsDialog: React.FunctionComponent = () => {
    const { openingEvent, closeDialog, propsForBloomDialog } =
        useEventLaunchedBloomDialog(kCollectionSettingsDialogId);

    // The launch event may name the page to open on.
    const initialPageKey = openingEvent?.initialPageKey as string | undefined;

    const [loadedSettings, setLoadedSettings] =
        React.useState<ICollectionSettingsResponse>();
    const [currentValues, setCurrentValues] =
        React.useState<ICollectionSettingsValues>();
    const [saving, setSaving] = React.useState(false);

    // Config-r can call onChange while rendering, so state updates from it are deferred; the OK
    // handler reads this ref to be sure it has the newest values.
    const latestValuesRef = React.useRef<ICollectionSettingsValues>();

    // Each open starts from the collection's current values, so the GET runs on every open.
    React.useEffect(() => {
        if (!propsForBloomDialog.open) {
            // Forget the closing open's values. Config-r captures initialValues when the pane
            // mounts, so if we left them here the next open would mount the pane on the previous
            // open's data and go on editing (and saving) that until the new GET arrived.
            latestValuesRef.current = undefined;
            setLoadedSettings(undefined);
            setCurrentValues(undefined);
            setSaving(false);
            return;
        }
        // We don't guard against this reply arriving after a quick close and reopen: it comes from
        // the local Bloom in milliseconds and carries the same collection's values anyway.
        get("collection/settings", (result) => {
            const response = result.data as ICollectionSettingsResponse;
            latestValuesRef.current = response.values;
            setLoadedSettings(response);
            setCurrentValues(response.values);
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
    const restartMessage = useL10n(
        "Bloom will close and re-open this project with the new settings.",
        "CollectionSettingsDialog.RestartMessage",
    );

    // C# names these pageKeys when it asks us to open on a particular page. Each built page is a
    // hook in its own file under settingsPages/ that returns its ConfigrPage (Config-R rejects a
    // page wrapped in a component); each placeholder below moves to one when its tab card adds it.
    const placeholderPages = [
        { pageKey: "languages", label: languagesLabel },
        { pageKey: "frontBackMatter", label: frontBackMatterLabel },
        { pageKey: "subscription", label: subscriptionLabel },
        { pageKey: "teamCollection", label: teamCollectionLabel },
        { pageKey: "bloomLibrary", label: bloomLibraryLabel },
    ];
    const advancedPage = useAdvancedPage({ settings: loadedSettings });
    const experimentalPage = useExperimentalPage({
        dialogOpen: propsForBloomDialog.open,
        settings: loadedSettings,
    });

    // A Team Collection member who is not an administrator gets this instead of any settings.
    const notAllowedMessage = loadedSettings?.notAllowedMessage;

    // C# decides which paths need a restart (they come with the GET reply), so that rule lives in
    // one place; every path ends at a plain value, so !== is enough.
    function restartNeededFor(values: ICollectionSettingsValues | undefined) {
        return (
            loadedSettings !== undefined &&
            !notAllowedMessage &&
            values !== undefined &&
            loadedSettings.restartPaths.some(
                (path) =>
                    valueAtPath(loadedSettings.values, path) !==
                    valueAtPath(
                        advancedValuesToSave(values, loadedSettings.values),
                        path,
                    ),
            )
        );
    }
    const needsRestart = restartNeededFor(currentValues);

    function saveAndCloseDialog() {
        // Block the button while the save is in flight, so a second click cannot save (and
        // perhaps restart Bloom) a second time.
        setSaving(true);
        // Always post, even if nothing changed: saving the same values again is harmless, and it
        // keeps OK to a single path. The ref, not the deferred state, has the newest values, so
        // the restart flag is worked out from it too.
        // OK is enabled only once the values have loaded.
        const values = latestValuesRef.current!;
        postJson(
            "collection/settings",
            {
                values: advancedValuesToSave(values, loadedSettings!.values),
                restartRequired: restartNeededFor(values),
            },
            () => {
                // C# performs the restart itself if one is needed.
                closeDialog();
            },
            (error) => {
                // If the save fails outright, the user keeps whatever they were editing and can
                // try OK again; leaving the button disabled would strand them with no way out
                // but Cancel. Re-throwing keeps the failure going to Bloom's usual error
                // reporting, which is what happens when no error callback is supplied at all.
                setSaving(false);
                throw error;
            },
        );
    }

    return (
        <BloomDialog
            css={css`
                height: 100%;
                box-sizing: border-box;
                // The "not allowed" message is two lines, so it gets a dialog sized to fit.
                ${notAllowedMessage ? "" : kConfigrDialogSizeCss}
            `}
            {...propsForBloomDialog}
            onClose={closeDialog}
            // Cancel is not blocked while a save is in flight: the local save takes milliseconds,
            // and the worst case is that the OK the user already pressed takes effect.
            onCancel={closeDialog}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle} />
            <ConfigrDialogMiddle>
                {notAllowedMessage && (
                    <div
                        data-testid="not-allowed-message"
                        css={css`
                            white-space: pre-line;
                        `}
                    >
                        {notAllowedMessage}
                    </div>
                )}
                {loadedSettings && !notAllowedMessage && (
                    <ConfigrPane
                        className={kConfigrPaneClassName}
                        label={dialogTitle}
                        showAppBar={false}
                        showSearch={false}
                        initialValues={
                            loadedSettings.values as unknown as ConfigrValues
                        }
                        themeOverrides={kConfigrThemeOverrides}
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
                        {placeholderPages.map((page) => (
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
                        {advancedPage}
                        {experimentalPage}
                    </ConfigrPane>
                )}
            </ConfigrDialogMiddle>
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
                {notAllowedMessage ? (
                    <DialogCloseButton onClick={closeDialog} />
                ) : (
                    <React.Fragment>
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
                    </React.Fragment>
                )}
            </DialogBottomButtons>
        </BloomDialog>
    );
};
