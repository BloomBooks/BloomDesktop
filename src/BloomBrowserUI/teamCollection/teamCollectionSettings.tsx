import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "../react_components/l10nComponents";
import "./teamCollectionSettings.less";

// A device for getting code into the team collection module
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
export { ProgressDialog };
import { NewTeamCollection } from "./NewTeamCollection";
export { NewTeamCollection };
import { CreateTeamCollection } from "./CreateTeamCollection";
export { CreateTeamCollection };

import BloomButton from "../react_components/bloomButton";
import { Dialog } from "@material-ui/core";
import { useState } from "react";

//import joiningImage from "../images/joining-team-collection.png";

// The contents of the Team Collection panel of the Settings dialog.
// Currently based on an early mock-up, now superceded.

export const TeamCollectionSettings: React.FunctionComponent = props => {
    const urlParams = new URLSearchParams(window.location.search);
    const existingTcPath = urlParams.get("folder");
    return (
        <div id="teamCollection-settings">
            <Div
                l10nKey="TeamCollection.Intro"
                l10nParam0="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
                // Todo: once we have an actual video this should link to it! For now just another link to the document.
                l10nParam1="https://docs.google.com/document/d/1DOhy7hnmG37NzcQN8oP6NkXW_X3WU7YH4ez_P1hV1mo/edit?usp=sharing"
                temporarilyDisableI18nWarning={true}
            >
                Bloom's Team Collection system helps your team collaborate as
                you create, translate, and edit books. Find out [how it
                works(text)]({0}) and [how it works (video)]({1}).
            </Div>
            {existingTcPath ? (
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
                        href={
                            // It is claimed that with very weak security settings this might work in FF.
                            // We make it work in GeckoFx by hooking up our own click handler in the C# Browser class.
                            "file://///" + existingTcPath
                        }
                    >
                        {existingTcPath}
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
                                BloomApi.post(
                                    "help/Tasks/Team_collection/Add_teammate"
                                )
                            }
                        >
                            How to add someone to this Team Collection
                        </BloomButton>
                    </div>
                </div>
            ) : (
                // No existing team collection
                <div>
                    <Div
                        l10nKey="TeamCollection.StartingInstructions"
                        className="extra-space-above"
                        temporarilyDisableI18nWarning={true}
                    >
                        It is important that **only one person** on the team
                        create the Team Collection.
                    </Div>
                    <div className="align-right">
                        <BloomButton
                            className="align-right"
                            l10nKey="TeamCollection.CreateTeamCollection"
                            enabled={true}
                            hasText={true}
                            variant="outlined"
                            onClick={() =>
                                BloomApi.post("teamCollection/showCreateDialog")
                            }
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
                        Has someone in your team already created a Team
                        Collection for your Team?
                    </Div>
                    <div className="align-right">
                        <BloomButton
                            l10nKey="TeamCollection.Joining"
                            enabled={true}
                            hasText={true}
                            variant="text"
                            temporarilyDisableI18nWarning={true}
                            onClick={() =>
                                BloomApi.post(
                                    "help/Tasks/Team_collection/Join_team"
                                )
                            }
                        >
                            How to join an existing Team Collection
                        </BloomButton>
                    </div>
                </div>
            )}
        </div>
    );
};

// allow plain 'ol javascript in the html to connect up react
(window as any).connectTeamCollectionSettingsScreen = element => {
    ReactDOM.render(<TeamCollectionSettings />, element);
};
