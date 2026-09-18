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

/** Applies a tab change to a cloned settings object and publishes the updated draft. */
export const updateSettings = (
    props: {
        settings: ReaderSettings;
        setSettings: (value: ReaderSettings) => void;
    },
    update: (settings: ReaderSettings) => void,
) => {
    const updatedSettings = cloneReaderSettings(props.settings);
    update(updatedSettings);
    props.setSettings(updatedSettings);
};
