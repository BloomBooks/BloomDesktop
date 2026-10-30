import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrValues,
    type ConfigrPaneHandle,
    ConfigrPane,
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
    ICollectionSettingsReply,
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";
import { useAdvancedPage } from "./settingsPages/AdvancedPage";
import { useBloomLibraryPage } from "./settingsPages/BloomLibraryPage";
import { useExperimentalPage } from "./settingsPages/ExperimentalPage";
import { useFrontBackMatterPage } from "./settingsPages/FrontBackMatterPage";
import { useLanguagesPage } from "./settingsPages/LanguagesPage";
import { useSubscriptionPage } from "./settingsPages/SubscriptionPage";
import { useTeamCollectionPage } from "./settingsPages/TeamCollectionPage";
import {
    ISettingsPage,
    ISettingsPageContext,
} from "./settingsPages/settingsPage";

const kCollectionSettingsDialogId = "CollectionSettingsDialog";

// Help's topic for a page with none of its own (see ISettingsPage.helpTopic), and when no page is
// showing.
const kGeneralSettingsHelpTopic =
    "User_Interface/Dialog_boxes/Settings_dialog_box.htm";

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

    // Each open mounts the dialog afresh, so it starts from the collection's current values, and
    // nothing from an earlier open carries over: not Config-R's initial values (it captures them
    // when the pane mounts), nor anything a page fetched.
    if (!propsForBloomDialog.open) return null;
    return (
        <LoadingCollectionSettingsDialog
            openingEvent={openingEvent}
            closeDialog={closeDialog}
        />
    );
};

// Asks Bloom for the settings, then shows the dialog, or, to a Team Collection member who is not
// an administrator, why they may not change them. Nothing shows until Bloom answers, which takes
// milliseconds.
const LoadingCollectionSettingsDialog: React.FunctionComponent<{
    openingEvent: { initialPageKey?: string } | undefined;
    closeDialog: () => void;
}> = (props) => {
    const [reply, setReply] = React.useState<ICollectionSettingsReply>();
    // We don't guard against this reply arriving after a quick close: setting state on an
    // unmounted component does nothing.
    useMountEffect(() => {
        get("collection/settings", (result) =>
            setReply(result.data as ICollectionSettingsReply),
        );
    });

    if (!reply) return null;
    if ("notAllowedMessage" in reply)
        return (
            <NotAllowedDialog
                message={reply.notAllowedMessage}
                closeDialog={props.closeDialog}
            />
        );
    return (
        <OpenCollectionSettingsDialog
            settings={reply}
            openingEvent={props.openingEvent}
            closeDialog={props.closeDialog}
        />
    );
};

// What a user who may not change the collection's settings gets instead of them.
const NotAllowedDialog: React.FunctionComponent<{
    message: string;
    closeDialog: () => void;
}> = (props) => {
    const dialogTitle = useL10n(
        "Collection Settings",
        "CollectionSettingsDialog.Title",
    );
    return (
        <BloomDialog
            open={true}
            onClose={props.closeDialog}
            onCancel={props.closeDialog}
            draggable={false}
        >
            <DialogTitle title={dialogTitle} />
            <ConfigrDialogMiddle>
                <div
                    data-testid="not-allowed-message"
                    css={css`
                        white-space: pre-line;
                    `}
                >
                    {props.message}
                </div>
            </ConfigrDialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <DialogHelpButton
                        id="collectionSettingsHelpButton"
                        helpId={kGeneralSettingsHelpTopic}
                    />
                </DialogBottomLeftButtons>
                <DialogCloseButton onClick={props.closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

// The dialog, with the settings Bloom gave it.
const OpenCollectionSettingsDialog: React.FunctionComponent<{
    settings: ICollectionSettingsResponse;
    // The launch event, which may name the page to open on. A new one arrives if the dialog is
    // launched again while open.
    openingEvent: { initialPageKey?: string } | undefined;
    closeDialog: () => void;
}> = (props) => {
    const settings = props.settings;
    const initialPageKey = props.openingEvent?.initialPageKey;
    const closeDialog = props.closeDialog;

    const [currentValues, setCurrentValues] = React.useState(settings.values);
    const [saving, setSaving] = React.useState(false);
    // The sections of the values whose editors are not Config-R controls, as edited so far (see
    // ISettingsPageContext.editValues); withOutsideEdits puts them in.
    const [outsideEdits, setOutsideEdits] = React.useState<
        Partial<ICollectionSettingsValues>
    >({});
    const paneRef = React.useRef<ConfigrPaneHandle>(null);
    // The top-level page Config-R is showing, so Help can open that page's topic.
    const [shownPageKey, setShownPageKey] = React.useState<string>();

    // Config-r can call onChange while rendering, so state updates from it are deferred; the OK
    // handler reads this ref to be sure it has the newest values.
    const latestValuesRef = React.useRef(settings.values);

    // A launch that arrives while the dialog is already open comes from a subscription badge on
    // one of its own pages, asking for the Subscription page. This effect hands that launch on to
    // the open pane, which switches page keeping the user's edits. The launch that opened the
    // dialog needs nothing here: the pane opens on its page through
    // initiallySelectedTopLevelPageKey.
    React.useEffect(() => {
        if (initialPageKey) paneRef.current?.selectTopLevelPage(initialPageKey);
        // Only a new launch should do this.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.openingEvent]);

    const dialogTitle = useL10n(
        "Collection Settings",
        "CollectionSettingsDialog.Title",
    );
    const restartMessage = useL10n(
        "Bloom will close and re-open this project with the new settings.",
        "CollectionSettingsDialog.RestartMessage",
    );

    // Config-R's values with the sections edited outside Config-R, then any in `edit`, in place
    // of the ones loaded.
    function withOutsideEdits(
        values: ICollectionSettingsValues,
        edit: Partial<ICollectionSettingsValues> = {},
    ): ICollectionSettingsValues {
        return { ...values, ...outsideEdits, ...edit };
    }
    // The values as the user is editing them, and with the edited text trimmed as OK would.
    const currentValuesEdited = withOutsideEdits(currentValues);
    const currentValuesTrimmed = withEditedTextTrimmed(
        currentValuesEdited,
        settings.values,
    );

    const pageContext: ISettingsPageContext = {
        settings,
        values: currentValuesEdited,
        valuesToSave: currentValuesTrimmed,
        // Merged into the latest edits, so two edits before a render both count.
        editValues: (edit) =>
            setOutsideEdits((edits) => ({ ...edits, ...edit })),
        saveNow: saveAndCloseDialog,
    };
    // In the order the dialog lists them. Each is a hook in its own file under settingsPages/ (see
    // ISettingsPage).
    const pages: ISettingsPage[] = [
        useLanguagesPage(pageContext),
        useFrontBackMatterPage(pageContext),
        useSubscriptionPage(pageContext),
        useTeamCollectionPage(pageContext),
        useBloomLibraryPage(pageContext),
        useAdvancedPage(pageContext),
        useExperimentalPage(pageContext),
    ];

    // Trimmed values with each page's own values as it saves them (see ISettingsPage.valuesToSave):
    // exactly what C# will be sent.
    function withPagesValuesToSave(
        trimmedValues: ICollectionSettingsValues,
    ): ICollectionSettingsValues {
        return pages.reduce(
            (valuesSoFar, page) =>
                page.valuesToSave
                    ? page.valuesToSave(valuesSoFar)
                    : valuesSoFar,
            trimmedValues,
        );
    }

    // C# decides which paths need a restart (they come with the GET reply), so that rule lives in
    // one place; every path ends at a plain value, so !== is enough. Give it the values exactly as
    // they will be saved, so it judges what C# will receive.
    function restartNeededFor(valuesToSave: ICollectionSettingsValues) {
        return settings.restartPaths.some(
            (path) =>
                valueAtPath(settings.values, path) !==
                valueAtPath(valuesToSave, path),
        );
    }
    const needsRestart = restartNeededFor(
        withPagesValuesToSave(currentValuesTrimmed),
    );

    // `edit` replaces sections of the values being edited (see ISettingsPageContext.saveNow).
    function saveAndCloseDialog(edit: Partial<ICollectionSettingsValues> = {}) {
        // Block the button while the save is in flight, so a second click cannot save (and
        // perhaps restart Bloom) a second time.
        setSaving(true);
        // Always post, even if nothing changed: saving the same values again is harmless, and it
        // keeps OK to a single path. The ref, not the deferred state, has the newest values, so
        // the restart flag is worked out from it too.
        const valuesToSave = withPagesValuesToSave(
            withEditedTextTrimmed(
                withOutsideEdits(latestValuesRef.current, edit),
                settings.values,
            ),
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
                ${kConfigrDialogSizeCss}
            `}
            open={true}
            onClose={closeDialog}
            // Cancel is not blocked while a save is in flight: the local save takes milliseconds,
            // and the worst case is that the OK the user already pressed takes effect.
            onCancel={closeDialog}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle} />
            <ConfigrDialogMiddle>
                <ConfigrPane
                    ref={paneRef}
                    className={kConfigrPaneClassName}
                    label={dialogTitle}
                    showAppBar={false}
                    showSearch={false}
                    initialValues={settings.values as unknown as ConfigrValues}
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
                    {/* A list of elements needs keys; each page's pageKey is unique. */}
                    {pages.flatMap((page) =>
                        page.element
                            ? [
                                  React.cloneElement(page.element, {
                                      key: page.element.props.pageKey,
                                  }),
                              ]
                            : [],
                    )}
                </ConfigrPane>
            </ConfigrDialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <DialogHelpButton
                        id="collectionSettingsHelpButton"
                        helpId={
                            pages.find(
                                (page) =>
                                    page.element?.props.pageKey ===
                                    shownPageKey,
                            )?.helpTopic ?? kGeneralSettingsHelpTopic
                        }
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
                <DialogOkButton
                    default={true}
                    enabled={
                        !saving &&
                        // A page that refuses shows what is wrong.
                        pages.every((page) => page.canSave !== false)
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
            </DialogBottomButtons>
        </BloomDialog>
    );
};
