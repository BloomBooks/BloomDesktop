import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "../react_components/l10nComponents";
import Link from "../react_components/link";
import "./teamCollectionSettings.less";

// A device for getting code into the team collection module
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
export { ProgressDialog };
import { NewTeamCollection } from "./NewTeamCollection";
export { NewTeamCollection };

// The contents of the Team Collection panel of the Settings dialog.
// Currently based on an early mock-up, now superceded.

export const TeamCollectionSettings: React.FunctionComponent = props => {
    return (
        <div id="teamCollection-settings">
            <Div
                l10nKey="TeamCollection.Intro"
                temporarilyDisableI18nWarning={true}
            >
                Bloom's Team Collection system helps your team collaborate as
                you create, translate, and edit books.
            </Div>
            <Div
                l10nKey="TeamCollection.Starting"
                className="teamCollection-heading"
                temporarilyDisableI18nWarning={true}
            >
                Starting a new Team Collection
            </Div>
            <Div
                l10nKey="TeamCollection.StartingInstructions"
                temporarilyDisableI18nWarning={true}
            >
                Only one person on the team should create the Team Collection.
                Before creating it, you will need to have Dropbox installed on
                your computer.
            </Div>
            <Link
                l10nKey="TeamCollection.CreateTeamCollection"
                onClick={() =>
                    BloomApi.post("teamCollection/createTeamCollection")
                }
                temporarilyDisableI18nWarning={true}
            >
                Create a Team Collection
            </Link>
            <Div
                l10nKey="TeamCollection.Joining"
                className="teamCollection-heading"
                temporarilyDisableI18nWarning={true}
            >
                How to join an existing Team Collection
            </Div>
            <Div
                l10nKey="TeamCollection.JoiningInstructions"
                temporarilyDisableI18nWarning={true}
            >
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
