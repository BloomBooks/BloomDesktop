import * as React from "react";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";

/**
 * What the Collection Settings dialog gives every page. Each page is a hook in this folder that
 * takes this and returns an ISettingsPage, so the dialog knows nothing about any one page. The
 * dialog is mounted only while it is open, so a page can fetch what it needs when it mounts.
 */
export interface ISettingsPageContext {
    // The reply to the dialog's GET collection/settings.
    settings: ICollectionSettingsResponse;
    // All the values as the user is editing them, exactly as typed. An editor that is not a
    // Config-R control shows its value from here.
    values: ICollectionSettingsValues;
    // The same, as OK would save them now (edited text trimmed): what a page should judge by.
    // No page's own valuesToSave (see ISettingsPage) has been applied yet.
    valuesToSave: ICollectionSettingsValues;
    // For an editor that is not a Config-R control (Config-R keeps its own controls' values):
    // replaces these sections of the values being edited.
    editValues: (edit: Partial<ICollectionSettingsValues>) => void;
    // Saves now, with these sections replaced, as OK would (restarting if a change needs it).
    saveNow: (edit: Partial<ICollectionSettingsValues>) => void;
}

export interface ISettingsPage {
    // The page's ConfigrPage (Config-R rejects a page wrapped in a component); undefined when the
    // page is not offered.
    element: React.ReactElement | undefined;
    // The help topic Help opens while this page shows; a page without one gets the general
    // Settings topic.
    helpTopic?: string;
    // False while the page has a value that may not be saved (the page shows what is wrong);
    // OK is disabled then.
    canSave?: boolean;
    // The values with this page's own values as it saves them (an edited alphabet cleaned, say).
    // The dialog applies it after trimming the edited text, for both the POST and the restart
    // check. A page changes only its own values here.
    valuesToSave?: (
        values: ICollectionSettingsValues,
    ) => ICollectionSettingsValues;
}
