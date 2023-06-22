/** @jsx jsx **/
import { css, jsx } from "@emotion/react";
import * as React from "react";
import { SettingsGroup } from "./PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { Span } from "../../react_components/l10nComponents";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";

export const PublishVisibilityGroup: React.FunctionComponent<{}> = props => {
    return (
        <SettingsGroup
            label={useL10n("Visibility", "PublishTab.Upload.Visibility")}
        >
            <ApiCheckbox
                label={
                    <React.Fragment>
                        <div
                            css={css`
                                display: flex;
                                align-items: start; // align image with top of checkbox
                            `}
                        >
                            <img
                                css={css`
                                    margin-left: -9px; //The checkbox has some right-padding that we want to overlap
                                    padding-right: 3px;
                                `}
                                src="/bloom/publish/LibraryPublish/DRAFT-Stamp.svg"
                            />
                            <Span l10nKey="PublishTab.Upload.Draft">
                                Show this book only to reviewers with whom I
                                share the URL of this book.
                            </Span>
                        </div>
                    </React.Fragment>
                }
                apiEndpoint="publish/markAsDraft"
            />
        </SettingsGroup>
    );
};
