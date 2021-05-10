/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { P } from "../react_components/l10nComponents";
import {
    BloomEnterpriseAvailableContext,
    RequiresBloomEnterpriseWrapper
} from "../react_components/requiresBloomEnterprise";
import "./TeamCollectionSettingsPanel.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { kBloomBlue } from "../bloomMaterialUITheme";

import BloomButton from "../react_components/bloomButton";

import StarIcon from "@material-ui/icons/Star";
import {
    CreateTeamCollectionDialog,
    showCreateTeamCollectionDialog
} from "./CreateTeamCollection";
//import joiningImage from "../images/joining-team-collection.png";

// The contents of the Team Collection panel of the Settings dialog.

export const TeamCollectionSettingsPanel: React.FunctionComponent = props => {
    const [repoFolderPath] = BloomApi.useApiString(
        "teamCollection/repoFolderPath",
        ""
    );

    const intro: JSX.Element = (
        <div>
            <div
                css={css`
                    background-color: ${kBloomBlue};
                    padding: 10px;
                    margin-bottom: 21px !important;
                    &,
                    a {
                        color: white !important;
                    }
                `}
            >
                <StarIcon /> This is an <strong>experimental</strong> feature.
                Please contact us at{" "}
                <a
                    href="mailto:experimental@bloomlibrary.org?subject= Our interest in Team Collections"
                    target="blank"
                >
                    experimental@bloomlibrary.org
                </a>{" "}
                so that we can talk over your needs and make sure that this
                feature is ready for you.
            </div>
            <P
                l10nKey="TeamCollection.Intro"
                l10nParam0="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
                // Todo: once we have an actual video this should link to it! For now just another link to the document.
                l10nParam1="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
                temporarilyDisableI18nWarning={true}
            >
                Bloom's Team Collection system helps your team collaborate as
                you create, translate, and edit books. Read about how it works
                [here]( %0), or view this [video](%1).
            </P>
        </div>
    );

    const isTeamCollection: JSX.Element = (
        <div>
            <P
                l10nKey="TeamCollection.ThisIsATC"
                className="teamCollection-heading no-space-below"
                temporarilyDisableI18nWarning={true}
            >
                This is a Team Collection
            </P>
            <P
                l10nKey="TeamCollection.CloudLocation"
                temporarilyDisableI18nWarning={true}
                className="no-space-below"
            >
                Cloud Storage Folder Location:
            </P>
            <a
                className="directory-link"
                href=""
                onClick={e => {
                    e.preventDefault();
                    BloomApi.postJson("common/showInFolder", {
                        folderPath: repoFolderPath
                    });
                }}
            >
                {repoFolderPath}
            </a>
            <P
                l10nKey="TeamCollection.AddingHelp"
                temporarilyDisableI18nWarning={true}
            >
                Need help adding someone to your Team Collection?
            </P>
            <div className="align-right">
                <BloomButton
                    l10nKey="TeamCollection.HowToAddSomeone"
                    temporarilyDisableI18nWarning={true}
                    variant="text"
                    enabled={true}
                    hasText={true}
                    onClick={() =>
                        BloomApi.post(
                            "help/Tasks/Basic_tasks/Team_Collections/Add_someone_to_a_Team_Collection.htm"
                        )
                    }
                >
                    How to add someone to this Team Collection
                </BloomButton>
            </div>
        </div>
    );

    const isNotTeamCollection: JSX.Element = (
        <div>
            <P
                l10nKey="TeamCollection.StartingInstructions"
                className="extra-space-above"
                temporarilyDisableI18nWarning={true}
            >
                It is important that **only one person** on the team create the
                Team Collection.
            </P>
            <div className="align-right">
                <BloomButton
                    className="align-right"
                    l10nKey="TeamCollection.CreateTeamCollection"
                    enabled={true}
                    hasText={true}
                    variant="outlined"
                    onClick={() => showCreateTeamCollectionDialog()}
                    temporarilyDisableI18nWarning={true}
                >
                    Create a Team Collection
                </BloomButton>
            </div>
            <P
                l10nKey="TeamCollection.JoiningHelp"
                className="extra-space-above"
                temporarilyDisableI18nWarning={true}
            >
                Has someone in your team already created a Team Collection for
                your Team?
            </P>
            <div className="align-right">
                <BloomButton
                    l10nKey="TeamCollection.Joining"
                    enabled={true}
                    hasText={true}
                    variant="text"
                    temporarilyDisableI18nWarning={true}
                    onClick={() =>
                        BloomApi.post(
                            "help/Tasks/Basic_tasks/Team_Collections/Join_a_Team_Collection.htm"
                        )
                    }
                >
                    How to join an existing Team Collection
                </BloomButton>
            </div>
        </div>
    );

    return (
        <ThemeProvider theme={theme}>
            <div id="teamCollection-settings">
                <RequiresBloomEnterpriseWrapper>
                    <BloomEnterpriseAvailableContext.Consumer>
                        {enterpriseAvailable => (
                            <React.Fragment>
                                {intro}
                                {repoFolderPath
                                    ? isTeamCollection
                                    : isNotTeamCollection}
                                <CreateTeamCollectionDialog />
                            </React.Fragment>
                        )}
                    </BloomEnterpriseAvailableContext.Consumer>
                </RequiresBloomEnterpriseWrapper>
            </div>
        </ThemeProvider>
    );
};
