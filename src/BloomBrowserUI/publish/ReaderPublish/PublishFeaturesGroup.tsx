import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { useApiBoolean } from "../../utils/bloomApi";

export const PublishFeaturesGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    // const [motionEnabled] = useApiBoolean(
    //     "publish/android/canHaveMotionMode", // awkward to use a publish.android API here in upload screen, but want same implementation
    //     false
    // );
    const [blindAccessibleEnabled] = useApiBoolean(
        "libraryPublish/blindAccessibleEnabled",
        false
    );
    const [signLanguageEnabled] = useApiBoolean(
        "libraryPublish/signLanguageEnabled",
        false
    );

    return (
        <SettingsGroup
            label={useL10n(
                "Advertise these special Features",
                "PublishTab.Upload.Advertise"
            )}
        >
            <FormGroup>
                <ApiCheckbox
                    english="Accessible to the Blind"
                    l10nKey="PublishTab.Upload.AccessibleToBlind"
                    apiEndpoint="libraryPublish/accessibleToBlind"
                    disabled={!blindAccessibleEnabled}
                    onChange={props.onChange}
                />
                {/* <ApiCheckbox label="Accessible to the visually impaired" /> */}
                <ApiCheckbox
                    english="Sign Language"
                    l10nKey="PublishTab.Upload.SignLanguage"
                    apiEndpoint="libraryPublish/signLanguage"
                    disabled={!signLanguageEnabled}
                />
                {/* <ApiCheckbox
                    english="Motion Book"
                    // Need new APIs here, similar but using different feature set. Or generalize somehow.
                    l10nKey="PublishTab.Android.MotionBookMode"
                    // tslint:disable-next-line:max-line-length
                    l10nComment="Motion Books are Talking Books in which the picture fills the screen, then pans and zooms while you hear the voice recording. This happens only if you turn the book sideways."
                    apiEndpoint="publish/android/motionBookMode"
                    onChange={props.onChange}
                    disabled={!motionEnabled}
                /> */}
            </FormGroup>
        </SettingsGroup>
    );
};
