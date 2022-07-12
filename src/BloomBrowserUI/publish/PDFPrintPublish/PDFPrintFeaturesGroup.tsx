import * as React from "react";
import FormGroup from "@material-ui/core/FormGroup";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomApi } from "../../utils/bloomApi";

export const PDFPrintFeaturesGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    return (
        <div>
            <SettingsGroup
                label={useL10n(
                    "Booklet Mode",
                    "PublishTab.PdfPrint.BookletModes"
                )}
            >
                <FormGroup>
                    {/* <ApiCheckbox
                    english="Motion Book"
                    l10nKey="PublishTab.Android.MotionBookMode"
                    // tslint:disable-next-line:max-line-length
                    l10nComment="Motion Books are Talking Books in which the picture fills the screen, then pans and zooms while you hear the voice recording. This happens only if you turn the book sideways."
                    apiEndpoint="publish/android/motionBookMode"
                    onChange={props.onChange}
                    disabled={!motionEnabled}
                /> */}
                </FormGroup>
            </SettingsGroup>
            <SettingsGroup
                label={useL10n(
                    "Prepare for Printshop",
                    "PublishTab.PdfPrint.PrintshopOptions"
                )}
            >
                <FormGroup />
            </SettingsGroup>
        </div>
    );
};
