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
                    {/*
                    Eventually this FormGroup will hold large buttons for Simple, Booklet Cover and
                    Booklet Insides PDF options.
                    */}
                </FormGroup>
            </SettingsGroup>
            <SettingsGroup
                label={useL10n(
                    "Prepare for Printshop",
                    "PublishTab.PdfPrint.PrintshopOptions"
                )}
            >
                <FormGroup>
                    {/*
                    I'm just creating the framework here. After we get the basic PDF creation
                    working, we'll add an ApiCheckbox for Full Bleed and a CMYK dropdown options field
                    inside of this FormGroup.
                    */}
                </FormGroup>
            </SettingsGroup>
        </div>
    );
};
