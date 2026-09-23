// Styling and the settings-update helper shared across the Decodable Reader setup dialog: its
// three tabs, and the smaller pieces the Decodable Stages tab is built from, each in its own
// file alongside this one.

import { css } from "@emotion/react";
import { ReaderSettings } from "../ReaderSettings";
import { cloneReaderSettings } from "./decodableStagesUtils";

/** The grey this dialog uses for anything that is not primary content. */
export const kMutedText = "#707477";

/** Explanatory text beside or under a control: hints, counts, empty-state messages. */
export const commonTextStyles = css`
    color: ${kMutedText};
    font-size: 9pt;
`;

/** The small uppercase label that sits above a control or a section of one. */
export const commonHeaderStyles = css`
    color: ${kMutedText};
    font-size: 8pt;
    font-weight: 700;
    letter-spacing: 0.03em;
    text-transform: uppercase;
`;

/**
 * Applies a change to a clone of the settings and publishes that clone as the new draft,
 * returning it.
 *
 * It returns the clone because a caller may need to act on the new settings in the same breath,
 * while `props.settings` still holds the old ones -- React has not re-rendered yet. Two do:
 * addNewStage selects the stage it just added, and removeSelectedStage asks whether any
 * remaining stage still uses the word-list file of the stage it removed before deleting that
 * file. Handing the clone back is what lets them stay on this one path; otherwise they would
 * clone, mutate and publish by hand, and there would be two ways to change the settings.
 */
export const updateSettings = (
    props: {
        settings: ReaderSettings;
        setSettings: (value: ReaderSettings) => void;
    },
    update: (settings: ReaderSettings) => void,
): ReaderSettings => {
    const updatedSettings = cloneReaderSettings(props.settings);
    update(updatedSettings);
    props.setSettings(updatedSettings);
    return updatedSettings;
};
