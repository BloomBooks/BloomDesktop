import * as React from "react";
import * as ReactDOM from "react-dom";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "../react_components/l10nComponents";
import "./NewTeamCollection.less";
import BloomButton from "../react_components/bloomButton";
import { ExclaimTriangle } from "../react_components/ExclaimTriangle";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";

const kBloomBlue = "#1d94a4";

// The contents of the dialog that comes up when double-clicking a .JoinBloomTC file.
// Two versions (create new local collection, and merge with existing local) are both handled here.
// The class is rendered by the connectNewTeamCollectionScreen into the root element in
// NewTeamCollection.html in an independent C# BrowserDialog.

export const NewTeamCollection: React.FunctionComponent = props => {
    const urlParams = new URLSearchParams(window.location.search);
    const existingCollection = !!urlParams.get("existingCollection");
    const collectionName = urlParams.get("name") ?? "missing";
    return (
        <ThemeProvider theme={theme}>
            <div id="new-team-collection">
                <Div
                    className="join-heading"
                    l10nKey="TeamCollection.JoinHeading"
                    l10nParam0={collectionName}
                >
                    Join the Team Collection "{0}"
                </Div>
                {existingCollection ? (
                    <div className="grow">
                        <Div
                            l10nKey="TeamCollection.Merging"
                            l10nParam0={collectionName}
                        >
                            Bloom will merge your existing "{0}" collection with
                            this Team Collection.
                        </Div>
                        <Div
                            l10nKey="TeamCollection.MergingExplanation"
                            temporarilyDisableI18nWarning={true}
                        >
                            The rest of the team will receive any unique books
                            found in that collection. Any books you have that
                            are already in this Team Collection will be moved to
                            the "Lost and Found" folder, unless Bloom can
                            determine that they are the same.
                        </Div>
                        <div className="icon-row">
                            <ExclaimTriangle
                                triangleColor={kBloomBlue}
                                exclaimColor="white"
                            />
                            <Div
                                l10nKey="TeamCollection.StartFresh"
                                l10nParam0={collectionName}
                            >
                                If instead you want to start fresh, click Cancel
                                and rename your existing "{0}" collection to
                                something different. Then try to join again.
                            </Div>
                        </div>
                    </div>
                ) : (
                    <div className="grow body-of-new">
                        <Div
                            l10nKey="TeamCollection.Creating"
                            temporarilyDisableI18nWarning={true}
                        >
                            Bloom will create a new collection on your computer
                            based on this Team Collection.
                        </Div>
                        <div className="auto-pad" />
                        <div className="icon-row">
                            <ExclaimTriangle
                                triangleColor={kBloomBlue}
                                exclaimColor="white"
                            />
                            <Div
                                l10nKey="TeamCollection.MergeInstead"
                                temporarilyDisableI18nWarning={true}
                                className="icon-row-grow"
                            >
                                If instead you want to merge a collection you
                                already have into this Team Collection, click
                                Cancel and rename the collection you want to
                                merge to the same as the Team Collection you are
                                joining. Then try to join again.
                            </Div>
                        </div>
                        <div className="auto-pad" />
                    </div>
                )}

                <div id="buttons" className="no-space-below">
                    <BloomButton
                        id="join-button"
                        className="join-buttons"
                        l10nKey={
                            existingCollection
                                ? "TeamCollection.JoinAndMerge"
                                : "TeamCollection.Join"
                        }
                        temporarilyDisableI18nWarning={true}
                        hasText={true}
                        enabled={true}
                        onClick={() => {
                            BloomApi.post("teamCollection/joinTeamCollection");
                        }}
                    >
                        {existingCollection ? "Join and Merge" : "Join"}
                    </BloomButton>
                    <BloomButton
                        id="cancel-button"
                        className="join-buttons"
                        l10nKey="Common.Cancel"
                        temporarilyDisableI18nWarning={true}
                        hasText={true}
                        enabled={true}
                        variant="outlined"
                        onClick={() => {
                            BloomApi.post("dialog/close");
                        }}
                    >
                        Cancel
                    </BloomButton>
                </div>
            </div>
        </ThemeProvider>
    );
};

// allow plain 'ol javascript in the html to connect up react
(window as any).connectNewTeamCollectionScreen = element => {
    ReactDOM.render(<NewTeamCollection />, element);
};
