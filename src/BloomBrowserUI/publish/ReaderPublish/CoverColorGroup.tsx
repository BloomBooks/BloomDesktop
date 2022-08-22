/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { ColorBlock } from "../../react_components/colorPickerDialog";
import { BloomApi } from "../../utils/bloomApi";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useL10n } from "../../react_components/l10nHooks";

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
    const [
        bookCoverColor,
        setBookCoverColor
    ] = BloomApi.useApiStringStatePromise("publish/android/backColor", "white");

    // I (gjm) was thinking this had to do with if a book is "unlocked", but apparently it's more to do
    // with whether it checked out in Team Collections or not.
    // Currently, there is not UI difference to indicate that the control is disabled. It just won't do
    // anything if the user clicks on it.
    const [canModifyCurrentBook] = BloomApi.useApiBoolean(
        "common/canModifyCurrentBook",
        false
    );

    const inStorybookMode = React.useContext(StorybookContext);
    const imagePath = "/bloom/api/publish/android/thumbnail?color=";

    return (
        <div
            css={css`
                display: flex;
                flex-direction: row;
            `}
        >
            <ColorBlock
                currentColor={bookCoverColor}
                onChange={async colorChoice => {
                    await setBookCoverColor(colorChoice);
                    if (props.onChange) {
                        props.onChange();
                    }

                    // if we're just in storybook, change the color even though the above axios call will fail
                    if (inStorybookMode) {
                        setBookCoverColor(colorChoice);
                    }
                }}
                width={94}
                localizedTitle={props.localizedTitle}
                noAlphaSlider={true}
                container={getDialogContainer()}
                disabled={!canModifyCurrentBook}
            />
            <div
                css={css`
                    margin-right: 30px;
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
                    src={imagePath + bookCoverColor}
                />
            </div>
        </div>
    );
};

function getDialogContainer(): Element | undefined {
    if (!document) return undefined; // nothing else to do
    let container = document.getElementById("modalDialogContainer");
    if (!container) {
        const div = document.createElement("div");
        div.id = "modalDialogContainer";
        container = document.body.appendChild(div);
    }
    return container;
}
