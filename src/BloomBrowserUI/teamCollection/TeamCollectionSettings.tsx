import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "../react_components/l10nComponents";
import Link from "../react_components/link";
import "./teamCollectionSettings.less";

// The contents of the Team Collection panel of the Settings dialog.
// Currently based on an early mock-up, now superceded.

export const TeamCollectionSettings: React.FunctionComponent = props => {
    return (
        <div id="teamCollection-settings">
            <Div l10nKey="TeamCollection.Intro">
                Bloom's Team Collection system helps your team collaborate as
                you create, translate, and edit books.
            </Div>
            <Div
                l10nKey="TeamCollection.Starting"
                className="teamCollection-heading"
            >
                Starting a new Team Collection
            </Div>
            <Div l10nKey="TeamCollection.StartingInstructions">
                Only one person on the team should create the Team Collection.
                Before creating it, you will need to have Dropbox installed on
                your computer.
            </Div>
            <Link
                l10nKey="TeamCollection.CreateTeamCollection"
                onClick={() =>
                    BloomApi.post("teamCollection/createTeamCollection")
                }
            >
                Create a Team Collection
            </Link>
            <Div
                l10nKey="TeamCollection.Joining"
                className="teamCollection-heading"
            >
                How to join an existing Team Collection
            </Div>
            <Div l10nKey="TeamCollection.JoiningInstructions">
                First, the team leader should share the Team Collection's
                Dropbox folder with you. After that folder is synchronizing on
                your computer, open it and double click on "Join this Team
                Collection.JoinBloomTC" file.
            </Div>
            {/* <div className="button-row">

            </div> */}
        </div>
    );
};

// allow plain 'ol javascript in the html to connect up react
(window as any).connectTeamCollectionSettingsScreen = element => {
    ReactDOM.render(<TeamCollectionSettings />, element);
};
