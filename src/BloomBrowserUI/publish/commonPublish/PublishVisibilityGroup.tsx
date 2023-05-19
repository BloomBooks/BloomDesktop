/** @jsx jsx **/
import { jsx } from "@emotion/react";
import * as React from "react";
import { SettingsGroup } from "./PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { useApiBoolean } from "../../utils/bloomApi";
import { Span } from "../../react_components/l10nComponents";

export const PublishVisibilityGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    const [checked, setChecked] = useApiBoolean("publish/markAsDraft", false);
    return (
        <SettingsGroup
            label={useL10n("Visibility", "PublishTab.Android.Visibility")} // TODO what is this android localization thing?
        >
            {/* <ApiCheckbox
                english="DRAFT"
                l10nKey="PublishTab.Upload.Draft"
                apiEndpoint="publish/markAsDraft"
            /> */}
            <BloomCheckbox
                label={
                    <React.Fragment>
                        <img src="/bloom/publish/LibraryPublish/DRAFT-Stamp.svg" />
                        <Span l10nKey="PublishTab.Upload.Draft">
                            Show this book only to reviewers with whom I share
                            the URL of this book.
                        </Span>
                    </React.Fragment>
                }
                checked={checked}
                onCheckChanged={(newState: boolean | undefined) => {
                    setChecked(!!newState);
                    if (props.onChange) {
                        props.onChange();
                    }
                }}
            />
        </SettingsGroup>
    );
};
