import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "../react_components/l10nComponents";
import {
    BloomEnterpriseAvailableContext,
    RequiresBloomEnterpriseWrapper
} from "../react_components/requiresBloomEnterprise";
import "./TeamCollectionSettingsPanel.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";

// A device for getting code into the team collection module
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
export { ProgressDialog };
import { JoinTeamCollection } from "./JoinTeamCollection";
export { JoinTeamCollection };
import { CreateTeamCollection } from "./CreateTeamCollection";
export { CreateTeamCollection };

import BloomButton from "../react_components/bloomButton";
import { Dialog } from "@material-ui/core";
import { useState } from "react";

//import joiningImage from "../images/joining-team-collection.png";

// The contents of the Team Collection panel of the Settings dialog.

export const TeamCollectionSettingsPanel: React.FunctionComponent = props => {
    // Because this is the top level component in a C# web browser, there is nothing outside it that can
    // provide any props. We instead use a url param to communicate from C# to this whether we are showing
    // settings for a collection that is already part of a team collection
    // (and if so, to provide the collection path).
    const urlParams = new URLSearchParams(window.location.search);
    //const existingTcPath = urlParams.get("folder");
    const [createDlgOpen, setCreateDlgOpen] = useState(false);

    const [repoFolderPath] = BloomApi.useApiString(
        "teamCollection/repoFolderPath",
        ""
    );

    const intro: JSX.Element = (
        <Div
            l10nKey="TeamCollection.Intro"
            l10nParam0="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
            // Todo: once we have an actual video this should link to it! For now just another link to the document.
            l10nParam1="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
            temporarilyDisableI18nWarning={true}
        >
            Bloom's Team Collection system helps your team collaborate as you
            create, translate, and edit books. Read about how it works [here](
            {0}), or view this [video]({1}).
        </Div>
    );

    const isTeamCollection: JSX.Element = (
        <div>
            <Div
                l10nKey="TeamCollection.ThisIsATC"
                className="teamCollection-heading no-space-below"
                temporarilyDisableI18nWarning={true}
            >
                This is a Team Collection
            </Div>
            <Div
                l10nKey="TeamCollection.CloudLocation"
                temporarilyDisableI18nWarning={true}
                className="no-space-below"
            >
                Cloud Storage Folder Location:
            </Div>
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
            <Div
                l10nKey="TeamCollection.AddingHelp"
                temporarilyDisableI18nWarning={true}
            >
                Need help adding someone to your Team Collection?
            </Div>
            <div className="align-right">
                <BloomButton
                    l10nKey="TeamCollection.HowToAddSomeone"
                    temporarilyDisableI18nWarning={true}
                    variant="text"
                    enabled={true}
                    hasText={true}
                    onClick={() =>
                        BloomApi.post("help/Tasks/Team_collection/Add_teammate")
                    }
                >
                    How to add someone to this Team Collection
                </BloomButton>
            </div>
        </div>
    );

    const isNotTeamCollection: JSX.Element = (
        <div>
            <Div
                l10nKey="TeamCollection.StartingInstructions"
                className="extra-space-above"
                temporarilyDisableI18nWarning={true}
            >
                It is important that **only one person** on the team create the
                Team Collection.
            </Div>
            <div className="align-right">
                <BloomButton
                    className="align-right"
                    l10nKey="TeamCollection.CreateTeamCollection"
                    enabled={true}
                    hasText={true}
                    variant="outlined"
                    onClick={() => setCreateDlgOpen(true)}
                    temporarilyDisableI18nWarning={true}
                >
                    Create a Team Collection
                </BloomButton>
            </div>
            <Div
                l10nKey="TeamCollection.JoiningHelp"
                className="extra-space-above"
                temporarilyDisableI18nWarning={true}
            >
                Has someone in your team already created a Team Collection for
                your Team?
            </Div>
            <div className="align-right">
                <BloomButton
                    l10nKey="TeamCollection.Joining"
                    enabled={true}
                    hasText={true}
                    variant="text"
                    temporarilyDisableI18nWarning={true}
                    onClick={() =>
                        BloomApi.post("help/Tasks/Team_collection/Join_team")
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
                                <Dialog
                                    open={createDlgOpen}
                                    onBackdropClick={() =>
                                        setCreateDlgOpen(false)
                                    }
                                >
                                    <CreateTeamCollection
                                        closeDlg={() => setCreateDlgOpen(false)}
                                    />
                                </Dialog>
                            </React.Fragment>
                        )}
                    </BloomEnterpriseAvailableContext.Consumer>
                </RequiresBloomEnterpriseWrapper>
            </div>
        </ThemeProvider>
    );
};
