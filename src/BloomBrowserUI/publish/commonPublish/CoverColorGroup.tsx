/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import {
    ColorDisplayButton,
    DialogResult
} from "../../react_components/color-picking/colorPickerDialog";
import { useApiBoolean, useApiStringStatePromise } from "../../utils/bloomApi";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useL10n } from "../../react_components/l10nHooks";
import { useContext } from "react";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";

export const CoverColorGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const localizedTitle = useL10n(
        "Cover Color",
        "PublishTab.Android.CoverColor"
    );
    return (
        <SettingsGroup label={localizedTitle}>
            <CoverColorControl localizedTitle={localizedTitle} {...props} />
        </SettingsGroup>
    );
};

const CoverColorControl: React.FunctionComponent<{
    onChange?: () => void;
    localizedTitle: string;
}> = props => {
    // The old default cover color was "white". This made it impossible to tell if the color had been
    // updated or not. This works well. This api always returns SOME cover color.
    const [bookCoverColor, setBookCoverColor] = useApiStringStatePromise(
        "publish/backColor",
        ""
    );

    // I (gjm) was thinking this had to do with if a book is "unlocked", but apparently it's more to do
    // with whether it checked out in Team Collections or not.
    // Currently, there is no UI difference to indicate that the control is disabled. It just won't do
    // anything if the user clicks on it.
    const [canModifyCurrentBook] = useApiBoolean(
        "common/canModifyCurrentBook",
        false
    );

    // The 2 comic templates and the Video template have black covers and a 'meta' tag that preserves
    // the cover color, so it doesn't get changed. The color picker shouldn't function on these.
    const [hasPreserveCoverColor] = useApiBoolean(
        "common/hasPreserveCoverColor",
        false
    );

    const inStorybookMode = useContext(StorybookContext);

    async function setNewCover(colorChoice: string) {
        await setBookCoverColor(colorChoice);
        if (props.onChange) {
            props.onChange();
        }
        // if we're just in storybook, change the color even though the above axios call will fail
        if (inStorybookMode) {
            setBookCoverColor(colorChoice);
        }
    }

    const handleOnClose = (result: DialogResult, newColor: string) => {
        if (result === DialogResult.OK) {
            setNewCover(newColor);
        }
    };

    return (
        <div
            css={css`
                display: flex;
                flex-direction: row;
            `}
        >
            {/* Don't show the button until we get the background color */}
            {bookCoverColor !== "" && (
                <ColorDisplayButton
                    initialColor={bookCoverColor}
                    width={94}
                    localizedTitle={props.localizedTitle}
                    transparency={false}
                    disabled={!canModifyCurrentBook || hasPreserveCoverColor}
                    palette={BloomPalette.CoverBackground}
                    onClose={(result, newColor) =>
                        handleOnClose(result, newColor)
                    }
                />
            )}
            <div
                css={css`
                    margin-right: 10px;
                    margin-left: auto;
                `}
            >
                <img
                    css={css`
                        height: 50px;
                        width: 50px;
                    `}
                    // the api ignores the color parameter, but it
                    // causes this to re-request the img whenever the backcolor changes
                    src={"/bloom/api/publish/thumbnail?color=" + bookCoverColor}
                />
            </div>
        </div>
    );
};
