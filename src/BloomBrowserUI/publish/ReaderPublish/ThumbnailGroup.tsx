import * as React from "react";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { ColorChooser } from "../../react_components/colorChooser";
import { BloomApi } from "../../utils/bloomApi";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useL10n } from "../../react_components/l10nHooks";

export const ThumbnailGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => (
    <SettingsGroup label={useL10n("Thumbnail", "PublishTab.Android.Thumbnail")}>
        <ThumbnailControl {...props} />
    </SettingsGroup>
);

const ThumbnailControl: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const [bookCoverColor, setBookCoverColor] = BloomApi.useApiString(
        "publish/android/backColor",
        "white"
    );
    const inStorybookMode = React.useContext(StorybookContext);
    return (
        <ColorChooser
            menuLeft={true}
            imagePath="/bloom/api/publish/android/thumbnail?color="
            backColorSetting={bookCoverColor}
            onColorChanged={colorChoice => {
                setBookCoverColor(colorChoice);
                if (props.onChange) {
                    props.onChange();
                }

                // if we're just in storybook, change the color even though the above axios call will fail
                if (inStorybookMode) {
                    setBookCoverColor(colorChoice);
                }
            }}
        />
    );
};
