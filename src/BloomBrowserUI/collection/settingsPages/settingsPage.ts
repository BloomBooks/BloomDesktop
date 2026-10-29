import * as React from "react";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";

/**
 * What the Collection Settings dialog gives every page. Each page is a hook in this folder that
 * takes this and returns an ISettingsPage, so the dialog knows nothing about any one page.
 */
export interface ISettingsPageContext {
    dialogOpen: boolean;
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
    // All the values as the user is editing them, exactly as typed; undefined until they have
    // loaded, and for a user who may not edit the settings. An editor that is not a Config-R
    // control shows its value from here.
    values: ICollectionSettingsValues | undefined;
    // The same, as OK would save them now (edited text trimmed): what a page should judge by.
    valuesToSave: ICollectionSettingsValues | undefined;
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
    // The help topic Help opens while this page shows. Pages that took over a tab of the old
    // WinForms dialog keep that tab's topic; the others get the general Settings topic.
    helpTopic?: string;
    // False while the page has a value that may not be saved (the page shows what is wrong);
    // OK is disabled then.
    canSave?: boolean;
}
