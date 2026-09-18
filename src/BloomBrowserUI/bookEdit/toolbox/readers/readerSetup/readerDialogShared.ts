// Styling and the settings-update helper shared by the Decodable Reader setup dialog's tabs,
// which live one to a file alongside this one.

import { css } from "@emotion/react";
import { ReaderSettings } from "../ReaderSettings";
import { cloneReaderSettings } from "./decodableStagesUtils";

export const kMutedText = "#707477";

export const commonTextStyles = css`
    color: ${kMutedText};
    font-size: 9pt;
`;

export const commonHeaderStyles = css`
    color: ${kMutedText};
    font-size: 8pt;
    font-weight: 700;
    letter-spacing: 0.03em;
    text-transform: uppercase;
`;

/**
 * Applies a change to a cloned settings object and publishes the updated draft, returning it.
 *
 * It returns the new settings because several callers need them for something else in the same
 * breath -- which stage is now selected, what the reordered list looks like. Without that they
 * would have to clone, mutate and publish by hand, and then there would be two ways to change
 * the settings instead of one.
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
