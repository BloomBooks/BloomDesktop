/** @jsx jsx **/
import { jsx } from "@emotion/react";
import * as React from "react";
import { SettingsGroup } from "./PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { Span } from "../../react_components/l10nComponents";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";

export const PublishVisibilityGroup: React.FunctionComponent<{
    onChange?: () => void;
}> = props => {
    return (
        <SettingsGroup
            label={useL10n("Visibility", "PublishTab.Upload.Visibility")}
        >
            <ApiCheckbox
                label={
                    <React.Fragment>
                        <img src="/bloom/publish/LibraryPublish/DRAFT-Stamp.svg" />
                        <Span l10nKey="PublishTab.Upload.Draft">
                            Show this book only to reviewers with whom I share
                            the URL of this book.
                        </Span>
                    </React.Fragment>
                }
                apiEndpoint="publish/markAsDraft"
            />
        </SettingsGroup>
    );
};
