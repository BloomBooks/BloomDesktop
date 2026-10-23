import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrValues,
    type ConfigrPaneHandle,
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
    DialogHelpButton,
    DialogOkButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { get, postJson } from "../utils/bloomApi";
import { useMountEffect } from "../utils/useMountEffect";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsTeamCollection,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";
import { collectionSettingsHelpTopic } from "./collectionSettingsHelpTopic";
import { useAdvancedPage } from "./settingsPages/AdvancedPage";
import { useBloomLibraryPage } from "./settingsPages/BloomLibraryPage";
import { useExperimentalPage } from "./settingsPages/ExperimentalPage";
import { useFrontBackMatterPage } from "./settingsPages/FrontBackMatterPage";
import { useSubscriptionPage } from "./settingsPages/SubscriptionPage";
import {
    teamCollectionValuesAreValid,
    useTeamCollectionPage,
} from "./settingsPages/TeamCollectionPage";

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

// The values as they should be saved. Config-R does not trim what is typed, and C# saves exactly
// what we post, so every piece of text the user changed is trimmed here: a collection name typed
// with a trailing space would otherwise count as a rename, and Windows will not make a folder whose
// name ends in a space. Text the user did not touch goes back exactly as it was, so saving one
// setting never alters another value that already has surrounding spaces, nor one the user could
// not edit at all (a disabled field, such as a Team Collection's name). `valueWhenOpened` is the
// matching value as it was when the dialog opened.
function withEditedTextTrimmed<T>(value: T, valueWhenOpened: unknown): T {
    if (typeof value === "string")
        return (value === valueWhenOpened ? value : value.trim()) as T;
    if (value === null || typeof value !== "object") return value;
    // The value is an object: a group of named values (one page's, or one language's), each of
    // which is processed the same way. A branch that did not exist when the dialog opened (a
    // third language just added) has nothing to compare with, so all its text counts as edited.
    const whenOpened = (valueWhenOpened ?? {}) as Record<string, unknown>;
    return Object.fromEntries(
        Object.entries(value).map(([key, child]) => [
            key,
            withEditedTextTrimmed(child, whenOpened[key]),
        ]),
    ) as T;
}

// How many CollectionSettingsDialogs are mounted in this window: one in the workspace (App renders
// it), none in a window such as the WinForms Settings dialog's tabs.
let mountedDialogCount = 0;

/**
 * Opens the Collection Settings dialog, on the page named by initialPageKey if one is given.
 * App renders the dialog on every workspace tab, so this works from any of them, with no trip
 * through C#. C# can open it too, by sending LaunchDialog with the same id over the web socket.
 * Returns false, doing nothing, when no dialog is mounted in this window to receive the launch.
 */
export function showCollectionSettingsDialog(initialPageKey?: string): boolean {
    if (mountedDialogCount === 0) return false;
    document.dispatchEvent(
        new CustomEvent("LaunchDialog", {
            detail: { id: kCollectionSettingsDialogId, initialPageKey },
        }),
    );
    return true;
}

export const CollectionSettingsDialog: React.FunctionComponent = () => {
    const { openingEvent, closeDialog, propsForBloomDialog } =
        useEventLaunchedBloomDialog(kCollectionSettingsDialogId);

    // This effect records that this window has a dialog to receive launches, for
    // showCollectionSettingsDialog, for as long as the dialog is mounted.
    useMountEffect(() => {
        mountedDialogCount++;
        return () => {
            mountedDialogCount--;
        };
    });

    // The launch event may name the page to open on.
    const initialPageKey = openingEvent?.initialPageKey as string | undefined;

    const [loadedSettings, setLoadedSettings] =
        React.useState<ICollectionSettingsResponse>();
    const [currentValues, setCurrentValues] =
        React.useState<ICollectionSettingsValues>();
    const [saving, setSaving] = React.useState(false);
    // The subscription code as the user is typing it. It is kept here rather than in Config-R's
    // values because its editor is not a Config-R control; withSubscriptionCode puts it in.
    const [subscriptionCode, setSubscriptionCode] = React.useState<string>();
    const paneRef = React.useRef<ConfigrPaneHandle>(null);
    // The top-level page Config-R is showing, so Help can open that page's topic.
    const [shownPageKey, setShownPageKey] = React.useState<string>();
    // The Team Collection values as the user is editing them, kept here for the same reason.
    const [teamCollection, setTeamCollection] =
        React.useState<ICollectionSettingsTeamCollection>();

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
            setSubscriptionCode(undefined);
            setTeamCollection(undefined);
            setSaving(false);
            setShownPageKey(undefined);
            return;
        }
        // We don't guard against this reply arriving after a quick close and reopen: it comes from
        // the local Bloom in milliseconds and carries the same collection's values anyway.
        get("collection/settings", (result) => {
            const response = result.data as ICollectionSettingsResponse;
            latestValuesRef.current = response.values;
            setLoadedSettings(response);
            setCurrentValues(response.values);
            // A user who may not edit the settings gets no values.
            setSubscriptionCode(response.values?.subscription.code);
            setTeamCollection(response.values?.teamCollection);
        });
    }, [propsForBloomDialog.open]);

    // A launch that arrives while the dialog is already open comes from a subscription badge on
    // one of its own pages, asking for the Subscription page. This effect hands that launch on to
    // the open pane, which switches page keeping the user's edits. A launch that opens the dialog
    // finds no pane yet; the pane opens on its page through initiallySelectedTopLevelPageKey.
    React.useEffect(() => {
        if (initialPageKey) paneRef.current?.selectTopLevelPage(initialPageKey);
        // Only a new launch should do this.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [openingEvent]);

    const dialogTitle = useL10n(
        "Collection Settings",
        "CollectionSettingsDialog.Title",
    );
    const languagesLabel = useL10n(
        "Languages",
        "CollectionSettingsDialog.LanguageTab.LanguageTabLabel",
    );
    const restartMessage = useL10n(
        "Bloom will close and re-open this project with the new settings.",
        "CollectionSettingsDialog.RestartMessage",
    );

    // C# names these pageKeys when it asks us to open on a particular page. Each built page is a
    // hook in its own file under settingsPages/ that returns its ConfigrPage (Config-R rejects a
    // page wrapped in a component); each placeholder below moves to one when its tab card adds it.
    function placeholderPage(pageKey: string, label: string) {
        return (
            <ConfigrPage
                key={pageKey}
                label={label}
                pageKey={pageKey}
                topLevel={true}
            >
                <ConfigrGroup label={label}>
                    <ConfigrStatic>
                        <PagePlaceholder />
                    </ConfigrStatic>
                </ConfigrGroup>
            </ConfigrPage>
        );
    }
    const frontBackMatterPage = useFrontBackMatterPage({
        dialogOpen: propsForBloomDialog.open,
        settings: loadedSettings,
        values: currentValues,
    });
    const subscriptionPage = useSubscriptionPage({
        code: subscriptionCode,
        onCodeChange: setSubscriptionCode,
    });
    const teamCollectionPage = useTeamCollectionPage({
        settings: loadedSettings,
        values: teamCollection,
        onChange: setTeamCollection,
        // Compared as it would be saved, so spaces typed around the name do not count.
        // (A user who may not edit the settings gets null values.)
        collectionNameEdited:
            !!currentValues &&
            !!loadedSettings &&
            withEditedTextTrimmed(
                currentValues.advanced.collectionName,
                loadedSettings.values.advanced.collectionName,
            ) !== loadedSettings.values.advanced.collectionName,
        onCreateTeamCollection: (createInFolder) =>
            saveAndCloseDialog({ ...teamCollection!, createInFolder }),
    });
    const bloomLibraryPage = useBloomLibraryPage({
        dialogOpen: propsForBloomDialog.open,
        settings: loadedSettings,
    });
    const advancedPage = useAdvancedPage({ settings: loadedSettings });
    const experimentalPage = useExperimentalPage({
        dialogOpen: propsForBloomDialog.open,
        settings: loadedSettings,
    });

    // A Team Collection member who is not an administrator gets this instead of any settings.
    const notAllowedMessage = loadedSettings?.notAllowedMessage;

    // C# decides which paths need a restart (they come with the GET reply), so that rule lives in
    // one place; every path ends at a plain value, so !== is enough. Give it the values exactly as
    // they will be saved (see withEditedTextTrimmed), so it judges what C# will receive.
    function restartNeededFor(
        valuesToSave: ICollectionSettingsValues | undefined,
    ) {
        return (
            loadedSettings !== undefined &&
            !notAllowedMessage &&
            valuesToSave !== undefined &&
            loadedSettings.restartPaths.some(
                (path) =>
                    valueAtPath(loadedSettings.values, path) !==
                    valueAtPath(valuesToSave, path),
            )
        );
    }
    // Config-R's values with the values edited outside Config-R (the subscription code being
    // typed, and the Team Collection values) in place of the ones loaded.
    function withValuesEditedOutsideConfigr(
        values: ICollectionSettingsValues,
        teamCollectionValues = teamCollection!,
    ): ICollectionSettingsValues {
        return {
            ...values,
            subscription: { code: subscriptionCode! },
            teamCollection: teamCollectionValues,
        };
    }

    // Both are checked: a deferred onChange can set currentValues after a close has already
    // cleared loadedSettings.
    const needsRestart = restartNeededFor(
        currentValues &&
            loadedSettings &&
            withEditedTextTrimmed(
                withValuesEditedOutsideConfigr(currentValues),
                loadedSettings.values,
            ),
    );

    // teamCollectionValues replaces the Team Collection values being edited; creating a Team
    // Collection saves with the folder to create it in.
    function saveAndCloseDialog(teamCollectionValues = teamCollection!) {
        // Block the button while the save is in flight, so a second click cannot save (and
        // perhaps restart Bloom) a second time.
        setSaving(true);
        // Always post, even if nothing changed: saving the same values again is harmless, and it
        // keeps OK to a single path. The ref, not the deferred state, has the newest values, so
        // the restart flag is worked out from it too.
        // OK is enabled only once the values have loaded.
        const valuesToSave = withEditedTextTrimmed(
            withValuesEditedOutsideConfigr(
                latestValuesRef.current!,
                teamCollectionValues,
            ),
            loadedSettings!.values,
        );
        postJson(
            "collection/settings",
            {
                values: valuesToSave,
                restartRequired: restartNeededFor(valuesToSave),
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
                        ref={paneRef}
                        className={kConfigrPaneClassName}
                        label={dialogTitle}
                        showAppBar={false}
                        showSearch={false}
                        initialValues={
                            loadedSettings.values as unknown as ConfigrValues
                        }
                        themeOverrides={kConfigrThemeOverrides}
                        initiallySelectedTopLevelPageKey={initialPageKey}
                        onTopLevelPageChange={setShownPageKey}
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
                        {placeholderPage("languages", languagesLabel)}
                        {frontBackMatterPage}
                        {subscriptionPage}
                        {teamCollectionPage}
                        {bloomLibraryPage}
                        {advancedPage}
                        {experimentalPage}
                    </ConfigrPane>
                )}
            </ConfigrDialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <DialogHelpButton
                        id="collectionSettingsHelpButton"
                        helpId={collectionSettingsHelpTopic(shownPageKey)}
                    />
                    {needsRestart && (
                        <div
                            css={css`
                                align-self: center;
                            `}
                        >
                            {restartMessage}
                        </div>
                    )}
                </DialogBottomLeftButtons>
                {notAllowedMessage ? (
                    <DialogCloseButton onClick={closeDialog} />
                ) : (
                    <React.Fragment>
                        <DialogOkButton
                            default={true}
                            enabled={
                                currentValues !== undefined &&
                                // Set and cleared with loadedSettings, unlike currentValues.
                                teamCollection !== undefined &&
                                !saving &&
                                // The Team Collection page shows what is wrong.
                                teamCollectionValuesAreValid(
                                    loadedSettings!,
                                    teamCollection,
                                )
                            }
                            l10nKey={
                                needsRestart
                                    ? "CollectionSettingsDialog.Restart"
                                    : undefined
                            }
                            englishText={needsRestart ? "Restart" : undefined}
                            onClick={() => saveAndCloseDialog()}
                        />
                        <DialogCancelButton />
                    </React.Fragment>
                )}
            </DialogBottomButtons>
        </BloomDialog>
    );
};
